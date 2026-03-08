using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Services
{
    /// <summary>
    /// Executes read-only git commands (status, diff, log) against the configured
    /// repository root.  Never performs commits, pushes, or other mutations.
    /// </summary>
    public sealed class GitService : IGitService
    {
        private readonly McpServiceSettings _settings;
        private static readonly McpLogger _log = McpLogger.For<GitService>();

        // Maximum raw diff size returned to callers (2 MB).
        private const int MaxDiffBytes = 2 * 1024 * 1024;

        /// <summary>Initialises a new instance of <see cref="GitService"/>.</summary>
        public GitService(McpServiceSettings settings, PathValidator pathValidator)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // ── Public interface ─────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<bool> IsGitRepositoryAsync(CancellationToken ct = default)
        {
            // Fast check: look for .git directory / file (submodule worktrees use a file).
            var gitEntry = Path.Combine(_settings.RepositoryRoot, ".git");
            if (Directory.Exists(gitEntry) || File.Exists(gitEntry))
                return true;

            // Slow check: ask git itself.
            try
            {
                var result = await RunGitAsync("rev-parse --is-inside-work-tree", ct).ConfigureAwait(false);
                return result.ExitCode == 0 && result.Stdout.Trim() == "true";
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<GitStatusResponse> GetStatusAsync(GitStatusRequest request, CancellationToken ct = default)
        {
            var response = new GitStatusResponse();

            if (!await IsGitRepositoryAsync(ct).ConfigureAwait(false))
                return response;

            response.IsGitRepository = true;

            // Current branch.
            var branchResult = await RunGitAsync("rev-parse --abbrev-ref HEAD", ct).ConfigureAwait(false);
            if (branchResult.ExitCode == 0)
                response.CurrentBranch = branchResult.Stdout.Trim();

            // HEAD commit (SHA + subject).
            var headResult = await RunGitAsync("log -1 --format=%H%x1F%s", ct).ConfigureAwait(false);
            if (headResult.ExitCode == 0)
            {
                var parts = headResult.Stdout.Trim().Split('\x1F');
                if (parts.Length >= 1) response.HeadCommitSha     = parts[0].Trim();
                if (parts.Length >= 2) response.HeadCommitMessage = parts[1].Trim();
            }

            // Remote URL (best-effort — fails gracefully on repos with no origin).
            var remoteResult = await RunGitAsync("remote get-url origin", ct).ConfigureAwait(false);
            if (remoteResult.ExitCode == 0)
                response.RemoteUrl = remoteResult.Stdout.Trim();

            // File status.
            var pathSuffix = BuildPathSuffix(request?.Path);
            var statusResult = await RunGitAsync($"status --porcelain=v1{pathSuffix}", ct).ConfigureAwait(false);
            if (statusResult.ExitCode == 0)
            {
                var entries = ParsePorcelainStatus(statusResult.Stdout);
                response.Files              = entries;
                response.StagedCount        = entries.Count(e => e.IsStaged);
                response.UnstagedCount      = entries.Count(e => e.IsUnstaged);
                response.UntrackedCount     = entries.Count(e => e.IsUntracked);
                response.HasUncommittedChanges = entries.Count > 0;
            }

            return response;
        }

        /// <inheritdoc/>
        public async Task<GitDiffResponse> GetDiffAsync(GitDiffRequest request, CancellationToken ct = default)
        {
            request = request ?? new GitDiffRequest();

            if (!await IsGitRepositoryAsync(ct).ConfigureAwait(false))
                return new GitDiffResponse { Scope = request.Scope };

            var scopeArgs = BuildDiffScopeArgs(request.Scope);
            var contextArg = $"--unified={Math.Max(0, request.ContextLines)}";
            var pathSuffix = BuildPathSuffix(request.Path);
            var args = $"diff {scopeArgs} {contextArg}{pathSuffix}";

            var result = await RunGitAsync(args, ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return new GitDiffResponse { Scope = request.Scope };

            return ParseDiffOutput(result.Stdout, request.Scope);
        }

        /// <inheritdoc/>
        public Task<GitDiffResponse> GetDiffForFileAsync(
            string relativePath, string scope = "unstaged", CancellationToken ct = default)
        {
            return GetDiffAsync(new GitDiffRequest
            {
                Path  = relativePath ?? "",
                Scope = scope ?? "unstaged"
            }, ct);
        }

        /// <inheritdoc/>
        public async Task<GitLogResponse> GetLogAsync(GitLogRequest request, CancellationToken ct = default)
        {
            request = request ?? new GitLogRequest();

            if (!await IsGitRepositoryAsync(ct).ConfigureAwait(false))
                return new GitLogResponse();

            var maxCount = Math.Max(1, Math.Min(request.MaxCount, 200));
            var sb = new StringBuilder("log");
            sb.Append($" -n {maxCount}");
            // Unit-separator-delimited format to avoid splitting on |
            sb.Append(" --format=%H%x1F%h%x1F%an%x1F%ae%x1F%aI%x1F%cn%x1F%cI%x1F%s%x1F%b%x1F%P");
            sb.Append(" --name-only");

            if (!string.IsNullOrWhiteSpace(request.Ref))
                sb.Append($" {EscapeShellArg(request.Ref)}");
            if (!string.IsNullOrWhiteSpace(request.Author))
                sb.Append($" --author={EscapeShellArg(request.Author)}");
            if (!string.IsNullOrWhiteSpace(request.Since))
                sb.Append($" --after={EscapeShellArg(request.Since)}");
            if (!string.IsNullOrWhiteSpace(request.Until))
                sb.Append($" --before={EscapeShellArg(request.Until)}");

            sb.Append(BuildPathSuffix(request.Path));

            var result = await RunGitAsync(sb.ToString(), ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return new GitLogResponse();

            return ParseLogOutput(result.Stdout, maxCount);
        }

        /// <inheritdoc/>
        public async Task<GitShowResponse> GetCommitAsync(GitShowRequest request, CancellationToken ct = default)
        {
            request = request ?? new GitShowRequest();

            if (string.IsNullOrWhiteSpace(request.Sha))
                throw new ArgumentException("Sha is required.", nameof(request.Sha));

            // Accept only hex characters (full or abbreviated SHA).
            if (!IsValidSha(request.Sha))
                throw new ArgumentException($"Invalid SHA format: '{request.Sha}'.", nameof(request.Sha));

            if (!await IsGitRepositoryAsync(ct).ConfigureAwait(false))
                return new GitShowResponse { NotFound = true };

            // Run git show to get structured metadata (same unit-separator format as log).
            var metaArgs = $"show --no-patch --format=%H%x1F%h%x1F%an%x1F%ae%x1F%aI%x1F%cn%x1F%ce%x1F%cI%x1F%s%x1F%b%x1F%P --name-only {EscapeShellArg(request.Sha)}";
            var metaResult = await RunGitAsync(metaArgs, ct).ConfigureAwait(false);

            if (metaResult.ExitCode != 0)
                return new GitShowResponse { NotFound = true };

            var response = ParseShowOutput(metaResult.Stdout);
            if (response.NotFound)
                return response;

            // Optionally include the full diff patch.
            if (request.IncludeDiff)
            {
                var diffArgs = $"show --format=\"\" {EscapeShellArg(request.Sha)}";
                var diffResult = await RunGitAsync(diffArgs, ct).ConfigureAwait(false);
                if (diffResult.ExitCode == 0)
                {
                    var rawDiff = diffResult.Stdout;
                    if (rawDiff.Length > MaxDiffBytes)
                        rawDiff = rawDiff.Substring(0, MaxDiffBytes) + "\n... [diff truncated]";
                    response.Diff = rawDiff;
                }
            }

            return response;
        }

        // ── Process runner ───────────────────────────────────────────────────────

        /// <summary>
        /// Runs <c>git &lt;args&gt;</c> in the repository root directory and returns
        /// stdout, stderr, and the exit code.
        /// </summary>
        internal async Task<(string Stdout, string Stderr, int ExitCode)> RunGitAsync(
            string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "git",
                Arguments              = args,
                WorkingDirectory       = _settings.RepositoryRoot,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            };

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcs = new TaskCompletionSource<int>();

                process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };
                process.Exited             += (_, __) => tcs.TrySetResult(process.ExitCode);

                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    using (ct.Register(() => { try { process.Kill(); } catch { } tcs.TrySetCanceled(); }))
                    {
                        await tcs.Task.ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Error($"git {args} failed to start", ex);
                    return (string.Empty, ex.Message, -1);
                }

                return (stdoutBuilder.ToString(), stderrBuilder.ToString(), process.ExitCode);
            }
        }

        // ── Parsing helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Parses <c>git status --porcelain=v1</c> output into a list of status entries.
        /// Format per line: <c>XY path</c> or <c>XY old-path -> new-path</c> for renames.
        /// </summary>
        internal static List<GitStatusEntry> ParsePorcelainStatus(string output)
        {
            var entries = new List<GitStatusEntry>();
            if (string.IsNullOrEmpty(output)) return entries;

            foreach (var line in output.Split('\n'))
            {
                if (line.Length < 4) continue;

                var indexStatus   = line[0].ToString();
                var worktreeStatus = line[1].ToString();
                var rest          = line.Substring(3).Trim();

                // Renames: "old -> new"
                string relativePath = rest;
                string? oldPath      = null;
                var arrowIdx = rest.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrowIdx >= 0)
                {
                    oldPath      = rest.Substring(0, arrowIdx).Trim('"');
                    relativePath = rest.Substring(arrowIdx + 4).Trim('"');
                }
                else
                {
                    relativePath = rest.Trim('"');
                }

                var isUntracked = indexStatus == "?" && worktreeStatus == "?";
                var isStaged    = !isUntracked && indexStatus.Trim() != "" && indexStatus != " ";
                var isUnstaged  = !isUntracked && worktreeStatus.Trim() != "" && worktreeStatus != " ";

                entries.Add(new GitStatusEntry
                {
                    RelativePath  = relativePath,
                    IndexStatus   = indexStatus,
                    WorkTreeStatus= worktreeStatus,
                    OldPath       = oldPath,
                    IsStaged      = isStaged,
                    IsUnstaged    = isUnstaged,
                    IsUntracked   = isUntracked
                });
            }

            return entries;
        }

        /// <summary>
        /// Parses unified diff output from <c>git diff</c> into structured objects.
        /// </summary>
        internal static GitDiffResponse ParseDiffOutput(string diffText, string scope)
        {
            var response = new GitDiffResponse { Scope = scope ?? "unstaged" };

            if (string.IsNullOrEmpty(diffText))
                return response;

            // Truncate if too large.
            if (diffText.Length > MaxDiffBytes)
            {
                diffText = diffText.Substring(0, MaxDiffBytes);
                response.Truncated = true;
            }

            response.RawDiff = diffText;

            // Split on "diff --git" boundaries.
            var fileBlocks = Regex.Split(diffText, @"(?=^diff --git )", RegexOptions.Multiline);

            foreach (var block in fileBlocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;

                var fileDiff = ParseFileDiffBlock(block);
                if (fileDiff != null)
                {
                    response.Files.Add(fileDiff);
                    response.TotalLinesAdded   += fileDiff.LinesAdded;
                    response.TotalLinesRemoved += fileDiff.LinesRemoved;
                }
            }

            return response;
        }

        private static GitFileDiff? ParseFileDiffBlock(string block)
        {
            var lines = block.Split('\n');
            if (lines.Length == 0) return null;

            // Header: diff --git a/path b/path
            var headerMatch = Regex.Match(lines[0], @"^diff --git a/(.+) b/(.+)$");
            if (!headerMatch.Success) return null;

            var fileDiff = new GitFileDiff
            {
                RelativePath = headerMatch.Groups[2].Value.Trim(),
                ChangeType   = "modified"
            };

            int lineIdx = 1;

            // Parse metadata lines until we hit "--- " or "@@ ".
            while (lineIdx < lines.Length)
            {
                var l = lines[lineIdx];
                if (l.StartsWith("new file mode"))      { fileDiff.ChangeType = "added";   lineIdx++; continue; }
                if (l.StartsWith("deleted file mode"))  { fileDiff.ChangeType = "deleted"; lineIdx++; continue; }
                if (l.StartsWith("rename from "))       { fileDiff.OldPath = l.Substring("rename from ".Length).Trim(); fileDiff.ChangeType = "renamed"; lineIdx++; continue; }
                if (l.StartsWith("rename to "))         { fileDiff.RelativePath = l.Substring("rename to ".Length).Trim(); lineIdx++; continue; }
                if (l.StartsWith("similarity index"))   { lineIdx++; continue; }
                if (l.StartsWith("index "))             { lineIdx++; continue; }
                if (l.StartsWith("--- ") || l.StartsWith("@@ ")) break;
                lineIdx++;
            }

            // Skip "--- a/..." and "+++ b/..." lines.
            while (lineIdx < lines.Length && (lines[lineIdx].StartsWith("--- ") || lines[lineIdx].StartsWith("+++ ")))
                lineIdx++;

            // Parse hunks.
            GitDiffHunk? currentHunk = null;
            int oldLine = 0, newLine = 0;

            while (lineIdx < lines.Length)
            {
                var l = lines[lineIdx];

                var hunkHeader = Regex.Match(l, @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@(.*)");
                if (hunkHeader.Success)
                {
                    currentHunk = new GitDiffHunk
                    {
                        OldStart = int.Parse(hunkHeader.Groups[1].Value),
                        OldCount = hunkHeader.Groups[2].Success ? int.Parse(hunkHeader.Groups[2].Value) : 1,
                        NewStart = int.Parse(hunkHeader.Groups[3].Value),
                        NewCount = hunkHeader.Groups[4].Success ? int.Parse(hunkHeader.Groups[4].Value) : 1,
                        Header   = l.Trim()
                    };
                    fileDiff.Hunks.Add(currentHunk);
                    oldLine = currentHunk.OldStart;
                    newLine = currentHunk.NewStart;
                    lineIdx++;
                    continue;
                }

                if (currentHunk != null)
                {
                    if (l.StartsWith("+"))
                    {
                        currentHunk.Lines.Add(new GitDiffLine { Type = "added",   Text = l.Substring(1), NewLineNumber = newLine++ });
                        fileDiff.LinesAdded++;
                    }
                    else if (l.StartsWith("-"))
                    {
                        currentHunk.Lines.Add(new GitDiffLine { Type = "removed", Text = l.Substring(1), OldLineNumber = oldLine++ });
                        fileDiff.LinesRemoved++;
                    }
                    else if (l.StartsWith(" "))
                    {
                        currentHunk.Lines.Add(new GitDiffLine { Type = "context", Text = l.Substring(1), OldLineNumber = oldLine++, NewLineNumber = newLine++ });
                    }
                }

                lineIdx++;
            }

            return fileDiff;
        }

        /// <summary>
        /// Parses <c>git log --format=%H%x1F%h%x1F...  --name-only</c> output.
        /// Records are separated by blank lines; changed files follow after a blank line
        /// in the name-only section.
        /// </summary>
        internal static GitLogResponse ParseLogOutput(string output, int maxCount)
        {
            var response = new GitLogResponse();
            if (string.IsNullOrEmpty(output)) return response;

            // git log with --name-only produces blocks separated by blank lines.
            // Each block: format-line, blank, list-of-files, blank.
            // We split on the unit-separator (0x1F) in the format line.
            var lines = output.Split('\n');
            int i = 0;

            while (i < lines.Length && response.Commits.Count < maxCount)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) { i++; continue; }

                // Detect format line by presence of unit-separator.
                if (!line.Contains('\x1F')) { i++; continue; }

                var parts = line.Split('\x1F');
                if (parts.Length < 9) { i++; continue; }

                var entry = new GitLogEntry
                {
                    Sha           = parts[0].Trim(),
                    ShortSha      = parts[1].Trim(),
                    AuthorName    = parts[2].Trim(),
                    AuthorEmail   = parts[3].Trim(),
                    CommitterName = parts[5].Trim(),
                    Subject       = parts[7].Trim(),
                    Body          = parts[8].Trim()
                };

                if (DateTimeOffset.TryParse(parts[4].Trim(), out var aDate)) entry.AuthorDate    = aDate;
                if (DateTimeOffset.TryParse(parts[6].Trim(), out var cDate)) entry.CommitterDate = cDate;

                // Parent SHAs are space-separated.
                if (parts.Length >= 10 && !string.IsNullOrWhiteSpace(parts[9]))
                    entry.ParentShas = parts[9].Trim().Split(' ').Where(p => !string.IsNullOrEmpty(p)).ToList();

                i++;

                // Collect changed files (non-empty lines until next format line or end).
                while (i < lines.Length)
                {
                    var fl = lines[i].Trim();
                    if (fl.Contains('\x1F')) break;   // next commit's format line
                    if (!string.IsNullOrEmpty(fl))
                        entry.ChangedFiles.Add(fl);
                    i++;
                }

                response.Commits.Add(entry);
            }

            return response;
        }

        // ── Small helpers ────────────────────────────────────────────────────────

        internal static string BuildPathSuffix(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return "";
            // Replace back-slashes with forward-slashes for git.
            var gitPath = relativePath.Replace('\\', '/').Trim('/');
            // Windows file paths cannot legally contain " or NUL.
            // Reject both to prevent argument injection via ProcessStartInfo.Arguments.
            if (gitPath.IndexOf('"') >= 0 || gitPath.IndexOf('\0') >= 0)
                return ""; // silently drop a malicious suffix — git will run without a path filter
            return $" -- \"{gitPath}\"";
        }

        internal static string BuildDiffScopeArgs(string scope)
        {
            switch ((scope ?? "unstaged").ToLowerInvariant())
            {
                case "staged":  return "--cached";
                case "head":    return "HEAD";
                case "unstaged":
                default:
                    // Check if scope looks like "commit:sha1..sha2".
                    if (scope != null && scope.StartsWith("commit:", StringComparison.OrdinalIgnoreCase))
                    {
                        // The commit range is user-supplied; wrap it safely so it cannot
                        // inject additional git options via unquoted whitespace or shell chars.
                        var range = scope.Substring("commit:".Length);
                        return EscapeShellArg(range);
                    }
                    return "";
            }
        }

        private static string EscapeShellArg(string value)
        {
            // For ProcessStartInfo.Arguments on Windows, wrap in double-quotes and escape inner double-quotes.
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// Parses <c>git show --no-patch --format=... --name-only</c> output for a single commit.
        /// Uses the same unit-separator (0x1F) format as <see cref="ParseLogOutput"/>,
        /// but also includes CommitterEmail (parts[6]).
        /// </summary>
        internal static GitShowResponse ParseShowOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
                return new GitShowResponse { NotFound = true };

            var lines = output.Split('\n');

            // Find the format line (contains unit-separator).
            string? formatLine = null;
            int fileStart = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Contains('\x1F'))
                {
                    formatLine = trimmed;
                    fileStart  = i + 1;
                    break;
                }
            }

            if (formatLine == null)
                return new GitShowResponse { NotFound = true };

            // %H %h %an %ae %aI %cn %ce %cI %s %b %P
            var parts = formatLine.Split('\x1F');
            if (parts.Length < 9)
                return new GitShowResponse { NotFound = true };

            var response = new GitShowResponse
            {
                Sha            = parts[0].Trim(),
                ShortSha       = parts[1].Trim(),
                AuthorName     = parts[2].Trim(),
                AuthorEmail    = parts[3].Trim(),
                CommitterName  = parts.Length > 5 ? parts[5].Trim() : "",
                CommitterEmail = parts.Length > 6 ? parts[6].Trim() : "",
                Subject        = parts.Length > 8 ? parts[8].Trim() : "",
                Body           = parts.Length > 9 ? parts[9].Trim() : "",
            };

            if (DateTimeOffset.TryParse(parts[4].Trim(), out var aDate)) response.AuthorDate    = aDate;
            if (parts.Length > 7 && DateTimeOffset.TryParse(parts[7].Trim(), out var cDate))    response.CommitterDate = cDate;

            if (parts.Length > 10 && !string.IsNullOrWhiteSpace(parts[10]))
                response.ParentShas = parts[10].Trim().Split(' ')
                    .Where(p => !string.IsNullOrEmpty(p)).ToList();

            // Collect changed files (non-empty, non-format lines after the format line).
            for (int i = fileStart; i < lines.Length; i++)
            {
                var fl = lines[i].Trim();
                if (!string.IsNullOrEmpty(fl) && !fl.Contains('\x1F'))
                    response.ChangedFiles.Add(fl);
            }

            return response;
        }

        /// <summary>Returns true when <paramref name="sha"/> is a valid git ref (4-40 hex chars or HEAD/branch-like name).</summary>
        private static bool IsValidSha(string sha)
        {
            if (string.IsNullOrWhiteSpace(sha)) return false;
            // Allow abbreviated SHAs (minimum 4 chars), full SHAs (40), and symbolic refs (HEAD, branches).
            // Reject anything containing characters that could escape the git argument quoting.
            foreach (var c in sha)
            {
                if (c == '"' || c == '\'' || c == '\\' || c == '\0' || c == '\n' || c == '\r' || c == ';' || c == '|' || c == '&' || c == '`' || c == ' ')
                    return false;
            }
            return sha.Length >= 4 && sha.Length <= 256;
        }
    }
}
