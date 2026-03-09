using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Full implementation of <see cref="ISearchService"/>.
    /// Uses direct file enumeration and in-process regex — no external indexer.
    /// </summary>
    public sealed class SearchService : ISearchService
    {
        private readonly McpServiceSettings _settings;
        private readonly PathValidator _pathValidator;
        private static readonly McpLogger _log = McpLogger.For<SearchService>();

        /// <summary>Initialises a new instance of <see cref="SearchService"/>.</summary>
        public SearchService(McpServiceSettings settings, PathValidator pathValidator)
        {
            _settings      = settings      ?? throw new ArgumentNullException(nameof(settings));
            _pathValidator = pathValidator  ?? throw new ArgumentNullException(nameof(pathValidator));
        }

        // ── search_text ──────────────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<SearchResponse> SearchTextAsync(SearchTextRequest request, CancellationToken ct = default)
        {
            ValidateRequest(request?.Query, "Query");

            // Escape the literal query to use as a regex pattern.
            var pattern = Regex.Escape(request!.Query);
            if (request.WholeWord)
                pattern = @"\b" + pattern + @"\b";

            var options = RegexOptions.Compiled | RegexOptions.Multiline;
            if (request.IgnoreCase) options |= RegexOptions.IgnoreCase;

            return RunSearchAsync(
                request,
                new Regex(pattern, options),
                request.Query,
                ct);
        }

        // ── search_regex ─────────────────────────────────────────────────────────

        // Maximum time allowed for a single regex execution against one file line.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

        /// <inheritdoc />
        public Task<SearchResponse> SearchRegexAsync(SearchRegexRequest request, CancellationToken ct = default)
        {
            ValidateRequest(request?.Pattern, "Pattern");

            var options = RegexOptions.Compiled;
            if (request!.IgnoreCase) options |= RegexOptions.IgnoreCase;
            if (request.Multiline)  options |= RegexOptions.Multiline;

            Regex regex;
            try
            {
                // Apply a per-match timeout to defend against ReDoS (catastrophic backtracking).
                regex = new Regex(request.Pattern, options, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid regex pattern: {ex.Message}", nameof(request.Pattern), ex);
            }

            return RunSearchAsync(request, regex, request.Pattern, ct);
        }

        // ── search_file_names ────────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<FileNameSearchResponse> SearchFileNamesAsync(SearchFileNamesRequest request, CancellationToken ct = default)
        {
            var validation = _pathValidator.Validate(request?.SearchPath ?? "", out var searchRoot);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!Directory.Exists(searchRoot))
                throw new DirectoryNotFoundException($"Search path not found: {request?.SearchPath}");

            var sw      = Stopwatch.StartNew();
            var pattern = request?.Pattern ?? "";
            bool isRegex    = PatternHelper.IsLikelyRegex(pattern);
            bool isWildcard = !isRegex && (pattern.Contains('*') || pattern.Contains('?'));

            Regex? compiledRegex = null;
            if (isRegex && pattern.Length > 0)
            {
                var opts = RegexOptions.Compiled | (request!.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
                compiledRegex = new Regex(pattern, opts);
            }

            var matches  = new List<FileNameMatch>();
            bool truncated = false;
            int maxResults = Math.Min(request!.MaxResults, _settings.MaxSearchResults);

            foreach (var filePath in EnumerateFiles(searchRoot, "*.*", ct))
            {
                var fi       = new FileInfo(filePath);
                var nameTest = request.MatchFullPath
                    ? _pathValidator.ToRelativePath(filePath)
                    : fi.Name;

                bool matched;
                if (isRegex)
                    matched = compiledRegex != null && compiledRegex.IsMatch(nameTest);
                else if (isWildcard)
                    matched = MatchesWildcard(nameTest, pattern, request.IgnoreCase);
                else
                    matched = nameTest.IndexOf(pattern, request.IgnoreCase
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal) >= 0;

                if (!matched) continue;

                matches.Add(new FileNameMatch
                {
                    RelativePath = _pathValidator.ToRelativePath(filePath),
                    Name         = fi.Name,
                    Extension    = fi.Extension.ToLowerInvariant(),
                    SizeBytes    = fi.Length
                });

                if (matches.Count >= maxResults) { truncated = true; break; }
            }

            sw.Stop();
            return Task.FromResult(new FileNameSearchResponse
            {
                Matches    = matches,
                TotalCount = matches.Count,
                Truncated  = truncated,
                ElapsedMs  = sw.ElapsedMilliseconds
            });
        }

        // ── search_by_extension ──────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<FileNameSearchResponse> SearchByExtensionAsync(SearchByExtensionRequest request, CancellationToken ct = default)
        {
            var exts = new HashSet<string>(
                (request.Extensions ?? new List<string>()).Select(e => e.StartsWith(".") ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            var validation = _pathValidator.Validate(request?.SearchPath ?? "", out var searchRoot);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            var sw      = Stopwatch.StartNew();
            var matches = new List<FileNameMatch>();
            bool truncated = false;
            int maxResults = Math.Min(request!.MaxResults, _settings.MaxSearchResults);

            foreach (var filePath in EnumerateFiles(searchRoot, "*.*", ct))
            {
                var fi = new FileInfo(filePath);
                if (exts.Count > 0 && !exts.Contains(fi.Extension)) continue;

                matches.Add(new FileNameMatch
                {
                    RelativePath = _pathValidator.ToRelativePath(filePath),
                    Name         = fi.Name,
                    Extension    = fi.Extension.ToLowerInvariant(),
                    SizeBytes    = fi.Length
                });

                if (matches.Count >= maxResults) { truncated = true; break; }
            }

            sw.Stop();
            return Task.FromResult(new FileNameSearchResponse
            {
                Matches    = matches,
                TotalCount = matches.Count,
                Truncated  = truncated,
                ElapsedMs  = sw.ElapsedMilliseconds
            });
        }

        // ── search_symbol_like ───────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<SearchResponse> SearchSymbolAsync(SearchSymbolRequest request, CancellationToken ct = default)
        {
            ValidateRequest(request?.SymbolName, "SymbolName");

            // Build a heuristic pattern for common code constructs.
            var escapedName = Regex.Escape(request!.SymbolName);
            string kind     = (request.SymbolKind ?? "").ToLowerInvariant();

            string patternStr;
            switch (kind)
            {
                case "class":      patternStr = $@"\bclass\s+{escapedName}\b";     break;
                case "interface":  patternStr = $@"\binterface\s+{escapedName}\b"; break;
                case "struct":     patternStr = $@"\bstruct\s+{escapedName}\b";    break;
                case "enum":       patternStr = $@"\benum\s+{escapedName}\b";      break;
                case "method":     patternStr = $@"\b{escapedName}\s*\(";          break;
                case "property":   patternStr = $@"\b{escapedName}\s*\{{";         break;
                default:           patternStr = $@"\b{escapedName}\b";             break;
            }

            var options = RegexOptions.Compiled | RegexOptions.Multiline;
            if (request.IgnoreCase) options |= RegexOptions.IgnoreCase;

            return RunSearchAsync(request, new Regex(patternStr, options), request.SymbolName, ct);
        }

        // ── find_references_like ─────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<SearchResponse> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct = default)
        {
            ValidateRequest(request?.IdentifierName, "IdentifierName");

            var escapedName = Regex.Escape(request!.IdentifierName);
            var pattern     = $@"\b{escapedName}\b";
            var options     = RegexOptions.Compiled | RegexOptions.Multiline;
            if (request.IgnoreCase) options |= RegexOptions.IgnoreCase;

            return RunSearchAsync(request, new Regex(pattern, options), request.IdentifierName, ct);
        }

        // ── Core search engine ───────────────────────────────────────────────────

        private async Task<SearchResponse> RunSearchAsync(
            SearchRequestBase request, Regex regex, string effectiveQuery,
            CancellationToken ct)
        {
            var validation = _pathValidator.Validate(request.SearchPath ?? "", out var searchRoot);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!Directory.Exists(searchRoot))
                throw new DirectoryNotFoundException($"Search path not found: {request.SearchPath}");

            int maxResults = Math.Min(
                request.MaxResults > 0 ? request.MaxResults : _settings.MaxSearchResults,
                _settings.MaxSearchResults);

            var sw            = Stopwatch.StartNew();
            var fileResults   = new List<FileSearchResult>();
            int totalMatches  = 0;
            bool truncated    = false;

            var extensions    = new HashSet<string>(
                (request.Extensions ?? new List<string>())
                    .Select(e => e.StartsWith(".") ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in EnumerateFiles(searchRoot, "*.*", ct))
            {
                if (ct.IsCancellationRequested) break;

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (extensions.Count > 0 && !extensions.Contains(ext)) continue;

                if (FileSystemService.IsBinaryFile(filePath)) continue;

                string[] lines;
                try { lines = await Task.Run(() => File.ReadAllLines(filePath), ct).ConfigureAwait(false); }
                catch { continue; }

                var fileMatches = new List<SearchMatch>();

                for (int i = 0; i < lines.Length; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    MatchCollection matchColl;
                    try
                    {
                        matchColl = regex.Matches(lines[i]);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        // This line caused catastrophic backtracking — skip it and continue.
                        _log.Warn($"Regex timeout on line {i + 1} of {filePath}; skipping line.");
                        continue;
                    }
                    if (matchColl.Count == 0) continue;

                    foreach (Match m in matchColl)
                    {
                        var before = GetContextLines(lines, i, -request.ContextLinesBefore, request.ContextLinesBefore);
                        var after  = GetContextLines(lines, i,  1,                           request.ContextLinesAfter);

                        fileMatches.Add(new SearchMatch
                        {
                            LineNumber     = i + 1,
                            Column         = m.Index,
                            MatchText      = m.Value,
                            LineText       = lines[i],
                            ContextBefore  = before,
                            ContextAfter   = after
                        });

                        totalMatches++;
                        if (totalMatches >= maxResults) { truncated = true; break; }
                    }

                    if (truncated) break;
                }

                if (fileMatches.Count > 0)
                {
                    fileResults.Add(new FileSearchResult
                    {
                        RelativePath = _pathValidator.ToRelativePath(filePath),
                        FileName     = Path.GetFileName(filePath),
                        MatchCount   = fileMatches.Count,
                        Matches      = fileMatches
                    });
                }

                if (truncated) break;
            }

            if (request.SortByPath)
                fileResults = fileResults.OrderBy(f => f.RelativePath).ToList();

            sw.Stop();
            return new SearchResponse
            {
                Files                  = fileResults,
                TotalMatchCount        = totalMatches,
                FilesWithMatchesCount  = fileResults.Count,
                Truncated              = truncated,
                EffectiveQuery         = effectiveQuery,
                ElapsedMs              = sw.ElapsedMilliseconds
            };
        }

        // ── File enumeration ─────────────────────────────────────────────────────

        private IEnumerable<string> EnumerateFiles(string rootDir, string searchPattern, CancellationToken ct)
        {
            var stack = new Stack<string>();
            stack.Push(rootDir);

            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) yield break;

                var dir = stack.Pop();
                var dirName = Path.GetFileName(dir);

                if (_settings.BlockedFolders.Contains(dirName)) continue;

                string[] files;
                try { files = Directory.GetFiles(dir, searchPattern); }
                catch { continue; }

                foreach (var f in files)
                {
                    if (_pathValidator.IsExtensionBlocked(f)) continue;
                    yield return f;
                }

                string[] subDirs;
                try { subDirs = Directory.GetDirectories(dir); }
                catch { continue; }

                foreach (var sub in subDirs.Reverse())
                    stack.Push(sub);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static List<string> GetContextLines(string[] lines, int currentIdx, int offset, int count)
        {
            var result = new List<string>();
            if (count <= 0) return result;

            int start = offset < 0 ? currentIdx + offset : currentIdx + offset;
            for (int i = 0; i < count; i++)
            {
                int idx = start + i;
                if (idx >= 0 && idx < lines.Length)
                    result.Add(lines[idx]);
            }
            return result;
        }

        private static void ValidateRequest(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"'{paramName}' must not be null or empty.", paramName);
        }

        private static bool MatchesWildcard(string input, string pattern, bool ignoreCase)
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            var options = RegexOptions.Compiled;
            if (ignoreCase) options |= RegexOptions.IgnoreCase;

            return Regex.IsMatch(input, regexPattern, options);
        }
    }
}
