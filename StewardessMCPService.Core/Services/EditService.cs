// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Full implementation of all write, patch, and structural-change operations.
    /// Every mutating method:
    ///   1. Validates the request and path (sandbox + read-only guard).
    ///   2. Optionally creates a timestamped backup and registers a rollback token.
    ///   3. In dry-run mode, produces a unified diff but touches nothing on disk.
    ///   4. Appends an audit log entry for every attempt (success, failure, or dry-run).
    /// </summary>
    public sealed class EditService : IEditService
    {
        private readonly McpServiceSettings _settings;
        private readonly PathValidator      _pathValidator;
        private readonly ISecurityService   _securityService;
        private readonly IAuditService      _auditService;

        // In-process rollback registry — maps GUID token -> backup metadata.
        // Static so tokens survive controller instantiation cycles within the same app domain.
        private static readonly ConcurrentDictionary<string, RollbackEntry> _rollbackRegistry
            = new ConcurrentDictionary<string, RollbackEntry>(StringComparer.Ordinal);

        // Rollback tokens expire after this duration to prevent unbounded memory growth.
        private static readonly TimeSpan RollbackTokenLifetime = TimeSpan.FromHours(2);

        // Maximum number of tokens to retain; oldest are evicted when the cap is hit.
        private const int MaxRollbackTokens = 500;

        private sealed class RollbackEntry
        {
            public string          TargetRelativePath { get; set; } = null!;
            public string          BackupAbsolutePath { get; set; } = null!;
            public DateTimeOffset  CreatedAt          { get; set; }
        }

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>Initialises a new instance of <see cref="EditService"/>.</summary>
        public EditService(McpServiceSettings settings, PathValidator pathValidator,
                           ISecurityService securityService, IAuditService auditService)
        {
            _settings        = settings        ?? throw new ArgumentNullException(nameof(settings));
            _pathValidator   = pathValidator   ?? throw new ArgumentNullException(nameof(pathValidator));
            _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
            _auditService    = auditService    ?? throw new ArgumentNullException(nameof(auditService));
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IEditService — single-file writes
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<EditResult> WriteFileAsync(WriteFileRequest request, CancellationToken ct = default)
        {
            if (request == null)                              throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path))     throw new ArgumentException("Path is required.",    nameof(request));
            if (request.Content == null)                     throw new ArgumentException("Content must not be null.", nameof(request));

            EnsureWriteAllowed();
            var absPath = ResolveWritePath(request.Path);
            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            var existingContent = File.Exists(absPath)
                ? await ReadFileTextAsync(absPath, ct)
                : null;

            var enc        = GetEncoding(request.Encoding);
            var newContent = NormalizeLineEndings(request.Content, request.LineEnding, existingContent);
            var diff       = GenerateUnifiedDiff(existingContent ?? string.Empty, newContent, relPath);

            if (opts.DryRun)
            {
                await Audit(opts, "write_file", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "write_file", diff, 1);
            }

            var (backupRel, token) = opts.CreateBackup && File.Exists(absPath)
                ? CreateBackup(absPath) : (null, null);

            await Task.Run(() =>
            {
                EnsureParentDirectory(absPath);
                File.WriteAllText(absPath, newContent, enc);
            }, ct);

            await Audit(opts, "write_file", relPath, AuditOutcome.Success, backupRel, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "write_file", diff, 1, backupRel, token);
        }

        /// <inheritdoc />
        public async Task<EditResult> CreateFileAsync(CreateFileRequest request, CancellationToken ct = default)
        {
            if (request == null)                          throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));

            EnsureWriteAllowed();
            var absPath = ResolveWritePath(request.Path);
            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            if (File.Exists(absPath) && !request.Overwrite)
                throw new IOException($"File already exists: {relPath}. Set Overwrite=true to replace it.");

            var enc        = GetEncoding(request.Encoding);
            var newContent = request.Content ?? string.Empty;
            var diff       = GenerateUnifiedDiff(string.Empty, newContent, relPath);

            if (opts.DryRun)
            {
                await Audit(opts, "create_file", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "create_file", diff, 1);
            }

            var (backupRel, token) = opts.CreateBackup && File.Exists(absPath)
                ? CreateBackup(absPath) : (null, null);

            await Task.Run(() =>
            {
                EnsureParentDirectory(absPath);
                File.WriteAllText(absPath, newContent, enc);
            }, ct);

            await Audit(opts, "create_file", relPath, AuditOutcome.Success, backupRel, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "create_file", diff, 1, backupRel, token);
        }

        /// <inheritdoc />
        public async Task<EditResult> CreateDirectoryAsync(CreateDirectoryRequest request, CancellationToken ct = default)
        {
            if (request == null)                          throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));

            EnsureWriteAllowed();
            var absPath = ResolveWritePath(request.Path);
            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            if (opts.DryRun)
            {
                await Audit(opts, "create_directory", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "create_directory", null, 1);
            }

            await Task.Run(() =>
            {
                if (request.CreateParents)
                {
                    Directory.CreateDirectory(absPath);
                }
                else
                {
                    var parent = Path.GetDirectoryName(absPath);
                    if (!Directory.Exists(parent))
                        throw new DirectoryNotFoundException($"Parent directory does not exist: {parent}");
                    Directory.CreateDirectory(absPath);
                }
            }, ct);

            await Audit(opts, "create_directory", relPath, AuditOutcome.Success, null, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "create_directory", null, 1, null, null);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IEditService — move / rename
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<EditResult> RenamePathAsync(RenamePathRequest request, CancellationToken ct = default)
        {
            if (request == null)                             throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path))    throw new ArgumentException("Path is required.",    nameof(request));
            if (string.IsNullOrWhiteSpace(request.NewName)) throw new ArgumentException("NewName is required.", nameof(request));
            if (request.NewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("NewName contains invalid characters.", nameof(request));

            EnsureWriteAllowed();
            var sourceAbs = ResolveWritePath(request.Path);
            var opts      = request.Options ?? new EditOptions();
            var relSource = _pathValidator.ToRelativePath(sourceAbs);
            var sw        = Stopwatch.StartNew();

            if (!File.Exists(sourceAbs) && !Directory.Exists(sourceAbs))
                throw new FileNotFoundException($"Source path not found: {relSource}");

            var destAbs = ResolveWritePath(
                _pathValidator.ToRelativePath(Path.Combine(Path.GetDirectoryName(sourceAbs)!, request.NewName)));
            var relDest = _pathValidator.ToRelativePath(destAbs);

            if (opts.DryRun)
            {
                await Audit(opts, "rename_path", relSource, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relSource, "rename_path", null, 1);
            }

            await Task.Run(() =>
            {
                if (File.Exists(sourceAbs))           File.Move(sourceAbs, destAbs);
                else if (Directory.Exists(sourceAbs)) Directory.Move(sourceAbs, destAbs);
            }, ct);

            await Audit(opts, "rename_path", relSource, AuditOutcome.Success, null, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relDest, "rename_path", null, 1, null, null);
        }

        /// <inheritdoc />
        public async Task<EditResult> MovePathAsync(MovePathRequest request, CancellationToken ct = default)
        {
            if (request == null)                                    throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.SourcePath))      throw new ArgumentException("SourcePath is required.",      nameof(request));
            if (string.IsNullOrWhiteSpace(request.DestinationPath)) throw new ArgumentException("DestinationPath is required.", nameof(request));

            EnsureWriteAllowed();
            var sourceAbs = ResolveWritePath(request.SourcePath);
            var destAbs   = ResolveWritePath(request.DestinationPath);
            var opts      = request.Options ?? new EditOptions();
            var relSource = _pathValidator.ToRelativePath(sourceAbs);
            var relDest   = _pathValidator.ToRelativePath(destAbs);
            var sw        = Stopwatch.StartNew();

            if (!File.Exists(sourceAbs) && !Directory.Exists(sourceAbs))
                throw new FileNotFoundException($"Source path not found: {relSource}");
            if (!request.Overwrite && (File.Exists(destAbs) || Directory.Exists(destAbs)))
                throw new IOException($"Destination already exists: {relDest}. Set Overwrite=true.");

            if (opts.DryRun)
            {
                await Audit(opts, "move_path", relSource, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relSource, "move_path", null, 1);
            }

            await Task.Run(() =>
            {
                EnsureParentDirectory(destAbs);
                if (File.Exists(sourceAbs))
                {
                    if (request.Overwrite && File.Exists(destAbs)) File.Delete(destAbs);
                    File.Move(sourceAbs, destAbs);
                }
                else
                {
                    Directory.Move(sourceAbs, destAbs);
                }
            }, ct);

            await Audit(opts, "move_path", relSource, AuditOutcome.Success, null, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relDest, "move_path", null, 1, null, null);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IEditService — deletion
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<EditResult> DeleteFileAsync(DeleteFileRequest request, CancellationToken ct = default)
        {
            if (request == null)                          throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));

            EnsureWriteAllowed();
            CheckApprovalIfRequired(request.ApprovalToken);
            var absPath = ResolveWritePath(request.Path);
            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {relPath}");

            if (opts.DryRun)
            {
                await Audit(opts, "delete_file", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "delete_file", null, 1);
            }

            var (backupRel, token) = opts.CreateBackup ? CreateBackup(absPath) : (null, null);
            await Task.Run(() => File.Delete(absPath), ct);

            await Audit(opts, "delete_file", relPath, AuditOutcome.Success, backupRel, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "delete_file", null, 1, backupRel, token);
        }

        /// <inheritdoc />
        public async Task<EditResult> DeleteDirectoryAsync(DeleteDirectoryRequest request, CancellationToken ct = default)
        {
            if (request == null)                          throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));

            EnsureWriteAllowed();
            CheckApprovalIfRequired(request.ApprovalToken);
            var absPath = ResolveWritePath(request.Path);
            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            if (!Directory.Exists(absPath))
                throw new DirectoryNotFoundException($"Directory not found: {relPath}");
            if (!request.Recursive && Directory.EnumerateFileSystemEntries(absPath).Any())
                throw new IOException($"Directory '{relPath}' is not empty. Set Recursive=true.");

            if (opts.DryRun)
            {
                await Audit(opts, "delete_directory", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "delete_directory", null, 1);
            }

            await Task.Run(() => Directory.Delete(absPath, request.Recursive), ct);

            await Audit(opts, "delete_directory", relPath, AuditOutcome.Success, null, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "delete_directory", null, 1, null, null);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IEditService — in-place edits
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<EditResult> AppendFileAsync(AppendFileRequest request, CancellationToken ct = default)
        {
            if (request == null)                          throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));
            if (request.Content == null)                  throw new ArgumentException("Content must not be null.", nameof(request));

            EnsureWriteAllowed();
            var absPath = ResolveWritePath(request.Path);
            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            var existing = File.Exists(absPath) ? await ReadFileTextAsync(absPath, ct) : string.Empty;

            // Ensure the appended text starts on a new line when requested.
            var appendText = request.Content;
            if (request.EnsureNewLine && existing.Length > 0
                && !existing.EndsWith("\n") && !existing.EndsWith("\r"))
            {
                appendText = Environment.NewLine + appendText;
            }

            var newContent = existing + appendText;
            var diff       = GenerateUnifiedDiff(existing, newContent, relPath);

            if (opts.DryRun)
            {
                await Audit(opts, "append_file", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "append_file", diff, 1);
            }

            var (backupRel, token) = opts.CreateBackup && File.Exists(absPath)
                ? CreateBackup(absPath) : (null, null);

            await Task.Run(() =>
            {
                EnsureParentDirectory(absPath);
                File.AppendAllText(absPath, appendText, new UTF8Encoding(false));
            }, ct);

            await Audit(opts, "append_file", relPath, AuditOutcome.Success, backupRel, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "append_file", diff, 1, backupRel, token);
        }

        /// <inheritdoc />
        public async Task<EditResult> ReplaceTextAsync(ReplaceTextRequest request, CancellationToken ct = default)
        {
            if (request == null)                           throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path))  throw new ArgumentException("Path is required.",    nameof(request));
            if (string.IsNullOrEmpty(request.OldText))    throw new ArgumentException("OldText is required.", nameof(request));
            if (request.NewText == null)                   throw new ArgumentException("NewText must not be null.", nameof(request));

            EnsureWriteAllowed();
            var absPath = ResolveWritePath(request.Path);
            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {_pathValidator.ToRelativePath(absPath)}");

            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            var original   = await ReadFileTextAsync(absPath, ct);
            var comparison = request.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var count = CountOccurrences(original, request.OldText, comparison);
            if (count == 0)
                throw new InvalidOperationException($"OldText was not found in '{relPath}'.");

            var newContent = ReplaceWithLimit(original, request.OldText, request.NewText,
                                              comparison, request.MaxReplacements);
            var replaced   = request.MaxReplacements > 0 ? Math.Min(count, request.MaxReplacements) : count;
            var diff       = GenerateUnifiedDiff(original, newContent, relPath);

            if (opts.DryRun)
            {
                await Audit(opts, "replace_text", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "replace_text", diff, replaced);
            }

            var (backupRel, token) = opts.CreateBackup ? CreateBackup(absPath) : (null, null);
            var enc = DetectFileEncoding(absPath);
            await Task.Run(() => File.WriteAllText(absPath, newContent, enc), ct);

            await Audit(opts, "replace_text", relPath, AuditOutcome.Success, backupRel, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "replace_text", diff, replaced, backupRel, token);
        }

        /// <inheritdoc />
        public async Task<EditResult> EditFileAsync(EditFileRequest request, CancellationToken ct = default)
        {
            if (request == null)                              throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path))     throw new ArgumentException("Path is required.",   nameof(request));
            if (request.Edits == null || request.Edits.Count == 0)
                throw new ArgumentException("At least one edit is required.", nameof(request));

            EnsureWriteAllowed();
            var absPath = ResolveWritePath(request.Path);
            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {_pathValidator.ToRelativePath(absPath)}");

            var opts    = request.Options ?? new EditOptions();
            opts.DryRun = opts.DryRun || request.DryRun;
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            var original = await ReadFileTextAsync(absPath, ct);
            var current  = original;

            for (int i = 0; i < request.Edits.Count; i++)
            {
                var edit = request.Edits[i];
                if (edit == null || edit.OldText == null)
                    throw new ArgumentException($"Edit[{i}].OldText must not be null.", nameof(request));
                if (edit.NewText == null)
                    throw new ArgumentException($"Edit[{i}].NewText must not be null.", nameof(request));

                if (!current.Contains(edit.OldText, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Edit[{i}].OldText was not found in '{relPath}': \"{Truncate(edit.OldText, 80)}\"");

                current = current.Replace(edit.OldText, edit.NewText, StringComparison.Ordinal);
            }

            var diff = GenerateUnifiedDiff(original, current, relPath);

            if (opts.DryRun)
            {
                await Audit(opts, "edit_file", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "edit_file", diff, request.Edits.Count);
            }

            var (backupRel, token) = opts.CreateBackup ? CreateBackup(absPath) : (null, null);
            var enc = DetectFileEncoding(absPath);
            await Task.Run(() => File.WriteAllText(absPath, current, enc), ct);

            await Audit(opts, "edit_file", relPath, AuditOutcome.Success, backupRel, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "edit_file", diff, request.Edits.Count, backupRel, token);
        }

        /// <inheritdoc />
        public async Task<EditResult> ReplaceLinesAsync(ReplaceLinesRequest request, CancellationToken ct = default)
        {
            if (request == null)                          throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.",           nameof(request));
            if (request.StartLine < 1)                    throw new ArgumentException("StartLine must be >= 1.",     nameof(request));
            if (request.EndLine < request.StartLine)      throw new ArgumentException("EndLine must be >= StartLine.", nameof(request));
            if (request.NewContent == null)               throw new ArgumentException("NewContent must not be null.", nameof(request));

            EnsureWriteAllowed();
            var absPath = ResolveWritePath(request.Path);
            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {_pathValidator.ToRelativePath(absPath)}");

            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            var lines = await Task.Run(() => new List<string>(File.ReadAllLines(absPath, Encoding.UTF8)), ct);
            if (request.StartLine > lines.Count)
                throw new ArgumentException(
                    $"StartLine {request.StartLine} exceeds file length {lines.Count}.", nameof(request));

            var endLine       = Math.Min(request.EndLine, lines.Count);
            var originalText  = string.Join(Environment.NewLine, lines);
            var affectedCount = endLine - request.StartLine + 1;

            var newLines = new List<string>(lines.Take(request.StartLine - 1));
            if (!string.IsNullOrEmpty(request.NewContent))
                newLines.AddRange(SplitLines(request.NewContent));
            newLines.AddRange(lines.Skip(endLine));

            var newContent = string.Join(Environment.NewLine, newLines);
            var diff       = GenerateUnifiedDiff(originalText, newContent, relPath);

            if (opts.DryRun)
            {
                await Audit(opts, "replace_lines", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "replace_lines", diff, affectedCount);
            }

            var (backupRel, token) = opts.CreateBackup ? CreateBackup(absPath) : (null, null);
            var enc = DetectFileEncoding(absPath);
            await Task.Run(() => File.WriteAllLines(absPath, newLines, enc), ct);

            await Audit(opts, "replace_lines", relPath, AuditOutcome.Success, backupRel, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "replace_lines", diff, affectedCount, backupRel, token);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IEditService — patch / diff
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<EditResult> PatchFileAsync(PatchFileRequest request, CancellationToken ct = default)
        {
            if (request == null)                          throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.",  nameof(request));
            if (string.IsNullOrWhiteSpace(request.Patch)) throw new ArgumentException("Patch is required.", nameof(request));

            EnsureWriteAllowed();
            var absPath = ResolveWritePath(request.Path);

            if (!File.Exists(absPath))
            {
                // Tier 2: try the full relative path embedded in the patch header
                // ("*** Update File: path/to/file.cs"  or  "+++ b/path/to/file.cs").
                var embeddedPath = TryExtractPathFromPatch(request.Patch);
                if (embeddedPath != null)
                {
                    var candidate = ResolveWritePath(embeddedPath);
                    if (File.Exists(candidate))
                        absPath = candidate;
                }
            }

            if (!File.Exists(absPath))
            {
                // Tier 3: search the entire repository for a file whose name matches
                // the bare filename supplied by the caller.
                // Succeed only when exactly one match is found — multiple matches are
                // ambiguous and must be reported as an error.
                var fileName = Path.GetFileName(request.Path);
                var matches  = FindFilesByName(_settings.RepositoryRoot, fileName);

                if (matches.Count > 1)
                {
                    var listed = string.Join("\n  ", matches.Select(m => _pathValidator.ToRelativePath(m)));
                    throw new InvalidOperationException(
                        $"Ambiguous path '{fileName}': {matches.Count} files found in the repository.\n" +
                        $"Provide the full relative path to resolve the ambiguity:\n  {listed}");
                }

                if (matches.Count == 1)
                    absPath = matches[0];
            }

            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {_pathValidator.ToRelativePath(absPath)}");

            var opts    = request.Options ?? new EditOptions();
            var relPath = _pathValidator.ToRelativePath(absPath);
            var sw      = Stopwatch.StartNew();

            var original   = await ReadFileTextAsync(absPath, ct);
            var newContent = ApplyPatch(original, request.Patch, request.FuzzFactor);
            var diff       = GenerateUnifiedDiff(original, newContent, relPath);

            if (opts.DryRun)
            {
                await Audit(opts, "patch_file", relPath, AuditOutcome.DryRun, null, sw.ElapsedMilliseconds, ct);
                return DryRunResult(relPath, "patch_file", diff, 1);
            }

            var (backupRel, token) = opts.CreateBackup ? CreateBackup(absPath) : (null, null);
            var enc = DetectFileEncoding(absPath);
            await Task.Run(() => File.WriteAllText(absPath, newContent, enc), ct);

            await Audit(opts, "patch_file", relPath, AuditOutcome.Success, backupRel, sw.ElapsedMilliseconds, ct);
            return SuccessResult(relPath, "patch_file", diff, 1, backupRel, token);
        }

        /// <inheritdoc />
        public async Task<BatchEditResult> ApplyDiffAsync(ApplyDiffRequest request, CancellationToken ct = default)
        {
            if (request == null)                         throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Diff)) throw new ArgumentException("Diff is required.", nameof(request));

            EnsureWriteAllowed();
            var opts       = request.Options ?? new EditOptions();
            var filePatches = ParseMultiFileDiff(request.Diff);
            if (filePatches.Count == 0)
                throw new ArgumentException("No valid file patches found in the diff.");

            var results    = new List<EditResult>();
            var batchResult = new BatchEditResult { WasDryRun = opts.DryRun };

            foreach (var (relFilePath, patchText) in filePatches)
            {
                try
                {
                    var result = await PatchFileAsync(new PatchFileRequest
                    {
                        Path       = relFilePath,
                        Patch      = patchText,
                        FuzzFactor = request.FuzzFactor,
                        Options    = opts
                    }, ct);
                    results.Add(result);
                    batchResult.SucceededCount++;
                }
                catch (Exception ex)
                {
                    results.Add(new EditResult
                    {
                        Success      = false,
                        RelativePath = relFilePath,
                        Operation    = "patch_file",
                        ErrorMessage = ex.Message
                    });
                    batchResult.FailedCount++;
                }
            }

            batchResult.Success = batchResult.FailedCount == 0;
            batchResult.Results = results;
            return batchResult;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IEditService — batch
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<BatchEditResult> ApplyBatchEditsAsync(BatchEditRequest request, CancellationToken ct = default)
        {
            if (request == null)                                    throw new ArgumentNullException(nameof(request));
            if (request.Edits == null || request.Edits.Count == 0) throw new ArgumentException("Edits list must not be empty.", nameof(request));

            EnsureWriteAllowed();
            var dryRun    = request.DryRun;
            var results   = new List<EditResult>();
            var rollbacks = new List<string>();   // tokens for compensating rollback on failure

            foreach (var item in request.Edits)
            {
                EditResult result;
                try
                {
                    result = await DispatchBatchItem(item, dryRun,
                                                     request.ChangeReason, request.SessionId, ct);
                    results.Add(result);
                    if (!dryRun && result.RollbackToken != null)
                        rollbacks.Add(result.RollbackToken);
                }
                catch (Exception ex)
                {
                    // On failure roll back all previously applied operations in this batch.
                    if (!dryRun && rollbacks.Count > 0)
                    {
                        foreach (var tok in rollbacks)
                            try { await RollbackAsync(new RollbackRequest { RollbackToken = tok }, ct); }
                            catch { /* best-effort */ }
                    }

                    results.Add(new EditResult
                    {
                        Success      = false,
                        RelativePath = item.Path,
                        Operation    = item.Operation,
                        ErrorMessage = ex.Message
                    });

                    return new BatchEditResult
                    {
                        Success        = false,
                        WasDryRun      = dryRun,
                        Results        = results,
                        SucceededCount = rollbacks.Count,
                        FailedCount    = 1,
                        WasRolledBack  = !dryRun && rollbacks.Count > 0,
                        RollbackReason = $"'{item.Operation}' on '{item.Path}' failed: {ex.Message}"
                    };
                }
            }

            var failed = results.Count(r => !r.Success);
            return new BatchEditResult
            {
                Success        = failed == 0,
                WasDryRun      = dryRun,
                Results        = results,
                SucceededCount = results.Count(r => r.Success),
                FailedCount    = failed
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IEditService — rollback
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<RollbackResult> RollbackAsync(RollbackRequest request, CancellationToken ct = default)
        {
            if (request == null)                                  throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.RollbackToken)) throw new ArgumentException("RollbackToken is required.", nameof(request));

            EnsureWriteAllowed();

            if (!_rollbackRegistry.TryRemove(request.RollbackToken, out var entry))
                throw new KeyNotFoundException(
                    $"Rollback token not found or already consumed: {request.RollbackToken}");

            // Reject expired tokens even if they were not yet evicted.
            if (entry.CreatedAt + RollbackTokenLifetime < DateTimeOffset.UtcNow)
                throw new UnauthorizedAccessException("Rollback token has expired.");

            if (!File.Exists(entry.BackupAbsolutePath))
                throw new FileNotFoundException($"Backup file no longer exists: {entry.BackupAbsolutePath}");

            var targetAbs = _pathValidator.ToAbsolutePath(entry.TargetRelativePath);
            var sw        = Stopwatch.StartNew();

            await Task.Run(() =>
            {
                EnsureParentDirectory(targetAbs);
                File.Copy(entry.BackupAbsolutePath, targetAbs, overwrite: true);
            }, ct);

            await _auditService.LogOperationAsync(
                requestId     : Guid.NewGuid().ToString("N"),
                sessionId     : null,
                operationType : AuditOperationType.Rollback,
                operationName : "rollback",
                clientIp      : "service",
                targetPath    : entry.TargetRelativePath,
                outcome       : AuditOutcome.Success,
                errorCode     : null,
                description   : $"Rolled back from backup: {entry.BackupAbsolutePath}",
                elapsedMs     : sw.ElapsedMilliseconds,
                ct            : ct);

            return new RollbackResult
            {
                Success            = true,
                RelativePath       = entry.TargetRelativePath,
                RestoredFromBackup = entry.BackupAbsolutePath
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IEditService — preview
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<PreviewChangesResponse> PreviewChangesAsync(PreviewChangesRequest request, CancellationToken ct = default)
        {
            if (request == null)                    throw new ArgumentNullException(nameof(request));
            if (request.ProposedEdits == null)      throw new ArgumentException("ProposedEdits is required.", nameof(request));

            request.ProposedEdits.DryRun = true;    // always force dry-run
            var batchResult = await ApplyBatchEditsAsync(request.ProposedEdits, ct);

            var previews = batchResult.Results.Select(r => new FilePreview
            {
                RelativePath = r.RelativePath,
                Operation    = r.Operation,
                UnifiedDiff  = r.Diff!,
                LinesAdded   = CountDiffLines(r.Diff, '+'),
                LinesRemoved = CountDiffLines(r.Diff, '-')
            }).ToList();

            var approvalToken = _securityService.GenerateApprovalToken(
                $"preview: {previews.Count} file(s)");

            return new PreviewChangesResponse
            {
                FilePreviews  = previews,
                ApprovalToken = approvalToken,
                TokenExpiry   = DateTimeOffset.UtcNow.AddMinutes(15)
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — security / write guards
        // ═══════════════════════════════════════════════════════════════════════

        private void EnsureWriteAllowed()
        {
            if (_settings.ReadOnlyMode)
                throw new InvalidOperationException("Service is configured in read-only mode.");
        }

        private void CheckApprovalIfRequired(string? token)
        {
            if (_settings.RequireApprovalForDestructive && !_securityService.ValidateApprovalToken(token!))
                throw new UnauthorizedAccessException(
                    "An approval token is required for this destructive operation.");
        }

        private string ResolveWritePath(string relativePath)
        {
            var check = _securityService.ValidateWritePath(relativePath, out var absPath);
            if (!check.IsAllowed)
                throw new UnauthorizedAccessException(check.ErrorMessage);
            return absPath;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — backup / rollback registry
        // ═══════════════════════════════════════════════════════════════════════

        private (string backupRelPath, string rollbackToken) CreateBackup(string absPath)
        {
            var backupRoot = Path.Combine(_settings.RepositoryRoot, ".mcp_backups");
            var relTarget  = _pathValidator.ToRelativePath(absPath);

            // Mirror the source file's directory structure inside .mcp_backups.
            var subDir = Path.Combine(backupRoot, Path.GetDirectoryName(relTarget) ?? string.Empty);
            Directory.CreateDirectory(subDir);

            var timestamp  = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var uid        = Guid.NewGuid().ToString("N").Substring(0, 8);
            var backupName = $"{timestamp}_{uid}_{Path.GetFileName(absPath)}";
            var backupAbs  = Path.Combine(subDir, backupName);

            File.Copy(absPath, backupAbs, overwrite: true);

            var relDir     = (Path.GetDirectoryName(relTarget) ?? string.Empty).Replace('\\', '/');
            var backupRel  = string.IsNullOrEmpty(relDir)
                ? $".mcp_backups/{backupName}"
                : $".mcp_backups/{relDir}/{backupName}";

            var token = Guid.NewGuid().ToString("N");
            RegisterRollbackToken(token, new RollbackEntry
            {
                TargetRelativePath = relTarget,
                BackupAbsolutePath = backupAbs,
                CreatedAt          = DateTimeOffset.UtcNow
            });

            return (backupRel, token);
        }

        /// <summary>
        /// Adds a rollback entry to the registry, first evicting expired entries and
        /// enforcing the cap to prevent unbounded memory growth.
        /// </summary>
        private static void RegisterRollbackToken(string token, RollbackEntry entry)
        {
            var cutoff = DateTimeOffset.UtcNow - RollbackTokenLifetime;

            // Evict expired entries.
            foreach (var kv in _rollbackRegistry)
                if (kv.Value.CreatedAt < cutoff)
                    _rollbackRegistry.TryRemove(kv.Key, out _);

            // If still over cap, evict the oldest entries.
            if (_rollbackRegistry.Count >= MaxRollbackTokens)
            {
                var oldest = _rollbackRegistry
                    .OrderBy(kv => kv.Value.CreatedAt)
                    .Take(_rollbackRegistry.Count - MaxRollbackTokens + 1)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in oldest)
                    _rollbackRegistry.TryRemove(k, out _);
            }

            _rollbackRegistry[token] = entry;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — unified diff generation (LCS-based)
        // ═══════════════════════════════════════════════════════════════════════

        internal static string GenerateUnifiedDiff(string originalContent, string newContent, string relPath)
        {
            if (string.Equals(originalContent, newContent, StringComparison.Ordinal))
                return string.Empty;

            var origLines = SplitLines(originalContent);
            var newLines  = SplitLines(newContent);
            var edits     = BuildEditScript(origLines, newLines);

            return FormatUnifiedDiff(edits, origLines, newLines, relPath, contextLines: 3);
        }

        private static string[] SplitLines(string content)
        {
            if (string.IsNullOrEmpty(content)) return Array.Empty<string>();
            return content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        // Returns an edit script: list of (op, origIndex, newIndex).
        // op = ' ' (common), '-' (removed), '+' (added).
        private static List<(char Op, int OrigIdx, int NewIdx)> BuildEditScript(string[] a, string[] b)
        {
            const int MaxLinesForLcs = 1500;
            int m = a.Length, n = b.Length;

            if (m > MaxLinesForLcs || n > MaxLinesForLcs)
            {
                // Large-file fallback: emit all lines as removed then added.
                var fb = new List<(char, int, int)>(m + n);
                for (int i = 0; i < m; i++) fb.Add(('-', i, -1));
                for (int j = 0; j < n; j++) fb.Add(('+', -1, j));
                return fb;
            }

            // O(m*n) LCS table.
            var dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    dp[i, j] = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal)
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);

            var result = new List<(char, int, int)>(m + n);
            int ai = m, bi = n;
            while (ai > 0 || bi > 0)
            {
                if (ai > 0 && bi > 0
                    && string.Equals(a[ai - 1], b[bi - 1], StringComparison.Ordinal))
                { result.Add((' ', ai - 1, bi - 1)); ai--; bi--; }
                else if (bi > 0 && (ai == 0 || dp[ai, bi - 1] >= dp[ai - 1, bi]))
                { result.Add(('+', -1, bi - 1)); bi--; }
                else
                { result.Add(('-', ai - 1, -1)); ai--; }
            }
            result.Reverse();
            return result;
        }

        private static string FormatUnifiedDiff(
            List<(char Op, int OrigIdx, int NewIdx)> edits,
            string[] origLines, string[] newLines,
            string relPath, int contextLines)
        {
            // Collect indices of changed edits.
            var changePositions = new List<int>();
            for (int i = 0; i < edits.Count; i++)
                if (edits[i].Op != ' ') changePositions.Add(i);

            if (changePositions.Count == 0) return string.Empty;

            // Group change positions into hunk ranges (merge if within 2*contextLines of each other).
            var hunks = new List<(int start, int end)>();
            int hs = changePositions[0], he = changePositions[0];
            for (int i = 1; i < changePositions.Count; i++)
            {
                if (changePositions[i] - he <= contextLines * 2 + 1)
                    he = changePositions[i];
                else { hunks.Add((hs, he)); hs = changePositions[i]; he = changePositions[i]; }
            }
            hunks.Add((hs, he));

            var sb = new StringBuilder();
            sb.AppendLine($"--- a/{relPath.Replace('\\', '/')}");
            sb.AppendLine($"+++ b/{relPath.Replace('\\', '/')}");

            foreach (var (rangeStart, rangeEnd) in hunks)
            {
                int hunkFrom = Math.Max(0, rangeStart - contextLines);
                int hunkTo   = Math.Min(edits.Count - 1, rangeEnd + contextLines);

                int origLine1 = -1, newLine1 = -1, origCount = 0, newCount = 0;
                var body = new List<string>();

                for (int k = hunkFrom; k <= hunkTo; k++)
                {
                    var (op, oi, ni) = edits[k];
                    var text = op == '+' ? newLines[ni] : origLines[oi];

                    if (origLine1 < 0 && (op == ' ' || op == '-')) origLine1 = oi + 1;
                    if (newLine1  < 0 && (op == ' ' || op == '+')) newLine1  = ni + 1;
                    if (op == ' ' || op == '-') origCount++;
                    if (op == ' ' || op == '+') newCount++;
                    body.Add($"{op}{text}");
                }

                if (origLine1 < 0) origLine1 = 1;
                if (newLine1  < 0) newLine1  = 1;

                sb.AppendLine($"@@ -{origLine1},{origCount} +{newLine1},{newCount} @@");
                foreach (var line in body)
                    sb.AppendLine(line);
            }

            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — patch application (unified diff parser)
        // ═══════════════════════════════════════════════════════════════════════

        internal static string ApplyPatch(string fileContent, string patchText, int fuzzFactor)
        {
            var lines = new List<string>(SplitLines(fileContent));
            var hunks = ParseHunks(patchText);
            int offset = 0;   // accumulates line-count delta as hunks are applied

            foreach (var hunk in hunks)
            {
                int target  = hunk.OldStart - 1 + offset;   // 0-based
                bool applied = false;

                for (int fuzz = 0; fuzz <= fuzzFactor && !applied; fuzz++)
                {
                    // Try exact position first, then ±fuzz.
                    var candidates = fuzz == 0
                        ? new[] { target }
                        : new[] { target + fuzz, target - fuzz };

                    foreach (int start in candidates)
                    {
                        if (start < 0 || start > lines.Count) continue;
                        if (!CanApplyHunk(lines, hunk, start)) continue;
                        ApplyHunk(lines, hunk, start);
                        int added   = hunk.Lines.Count(l => l.Op == '+');
                        int removed = hunk.Lines.Count(l => l.Op == '-');
                        offset  += added - removed;
                        applied  = true;
                        break;
                    }
                }

                if (!applied)
                    throw new InvalidOperationException(
                        $"Patch hunk at line {hunk.OldStart} could not be applied " +
                        $"(context mismatch even with fuzz={fuzzFactor}).");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private struct PatchHunk
        {
            public int                        OldStart;
            public int                        OldCount;
            public int                        NewStart;
            public int                        NewCount;
            public List<(char Op, string Text)> Lines;
        }

        private static List<PatchHunk> ParseHunks(string patchText)
        {
            var hunks = new List<PatchHunk>();
            var lines = patchText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int i     = 0;

            // Skip file-level header lines (--- / +++ / diff --git etc.)
            while (i < lines.Length && !lines[i].StartsWith("@@")) i++;

            while (i < lines.Length)
            {
                var headerLine = lines[i];
                if (!headerLine.StartsWith("@@")) { i++; continue; }

                var m = Regex.Match(headerLine, @"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
                if (!m.Success) { i++; continue; }

                var hunk = new PatchHunk
                {
                    OldStart = int.Parse(m.Groups[1].Value),
                    OldCount = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1,
                    NewStart = int.Parse(m.Groups[3].Value),
                    NewCount = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 1,
                    Lines    = new List<(char, string)>()
                };
                i++;

                while (i < lines.Length
                       && !lines[i].StartsWith("@@")
                       && !lines[i].StartsWith("--- ")
                       && !lines[i].StartsWith("+++ "))
                {
                    var hunkLine = lines[i++];
                    if (hunkLine.Length == 0
                        || hunkLine.StartsWith(@"\ No newline")) continue;
                    char op = hunkLine[0];
                    if (op == ' ' || op == '+' || op == '-')
                        hunk.Lines.Add((op, hunkLine.Substring(1)));
                }

                hunks.Add(hunk);
            }

            return hunks;
        }

        private static bool CanApplyHunk(List<string> lines, PatchHunk hunk, int startLine)
        {
            int idx = startLine;
            foreach (var (op, text) in hunk.Lines)
            {
                if (op == '+') continue;
                if (idx >= lines.Count) return false;
                if (!string.Equals(lines[idx].TrimEnd('\r'), text.TrimEnd('\r'), StringComparison.Ordinal))
                    return false;
                idx++;
            }
            return true;
        }

        private static void ApplyHunk(List<string> lines, PatchHunk hunk, int startLine)
        {
            var replacement = new List<string>();
            foreach (var (op, text) in hunk.Lines)
                if (op == '+' || op == ' ') replacement.Add(text);

            int removeCount = hunk.Lines.Count(l => l.Op == ' ' || l.Op == '-');
            int actualRemove = Math.Min(removeCount, lines.Count - startLine);

            lines.RemoveRange(startLine, actualRemove);
            lines.InsertRange(startLine, replacement);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — multi-file diff parser
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns absolute paths for every file in <paramref name="root"/> whose name
        /// (case-insensitive) equals <paramref name="fileName"/>.
        /// Hidden directories (prefixed with '.') and the backup folder are excluded.
        /// </summary>
        private static List<string> FindFilesByName(string root, string fileName)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(fileName))
                return results;

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, fileName,
                             new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 MatchCasing           = MatchCasing.CaseInsensitive,
                                 AttributesToSkip      = FileAttributes.System
                             }))
                {
                    // Skip the backup folder managed by this service.
                    var rel = file.Substring(root.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
                    if (rel.StartsWith(".mcp_backups", StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(file);
                }
            }
            catch (UnauthorizedAccessException) { /* skip inaccessible directories */ }

            return results;
        }

        /// <summary>
        /// Extracts a relative file path embedded in a patch text.
        /// Handles two common formats:
        ///   "*** Update File: path/to/file.cs"  (pseudo-patch / OpenAI apply-patch format)
        ///   "+++ b/path/to/file.cs"             (standard unified diff)
        ///   "+++ path/to/file.cs"               (unified diff without b/ prefix)
        /// Returns null if no path can be extracted.
        /// </summary>
        private static string? TryExtractPathFromPatch(string patchText)
        {
            if (string.IsNullOrWhiteSpace(patchText)) return null;

            foreach (var raw in patchText.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();

                // "*** Update File: path/to/file.cs"
                const string updateFile = "*** Update File:";
                if (line.StartsWith(updateFile, StringComparison.OrdinalIgnoreCase))
                {
                    var path = line.Substring(updateFile.Length).Trim();
                    if (!string.IsNullOrEmpty(path)) return path;
                }

                // "+++ b/path/to/file.cs" or "+++ path/to/file.cs"
                if (line.StartsWith("+++ ", StringComparison.Ordinal))
                {
                    var path = line.Substring(4).Trim();
                    if (path.StartsWith("b/", StringComparison.Ordinal)) path = path.Substring(2);
                    if (!string.IsNullOrEmpty(path) && path != "/dev/null") return path;
                }
            }

            return null;
        }

        private static List<(string RelPath, string PatchText)> ParseMultiFileDiff(string diffText)
        {
            var result   = new List<(string, string)>();
            var lines    = diffText.Replace("\r\n", "\n").Split('\n');
            int i        = 0;

            while (i < lines.Length)
            {
                if (!lines[i].StartsWith("--- ")) { i++; continue; }

                var fromLine = lines[i++];
                if (i >= lines.Length || !lines[i].StartsWith("+++ ")) continue;
                var toLine = lines[i++];

                // Extract target file path from "+++ b/path" or "+++ path"
                var filePath = toLine.Substring(4).Trim();
                if (filePath.StartsWith("b/")) filePath = filePath.Substring(2);
                if (filePath == "/dev/null") continue;  // file deleted — skip

                var patchLines = new List<string> { fromLine, toLine };
                while (i < lines.Length && !lines[i].StartsWith("--- "))
                    patchLines.Add(lines[i++]);

                result.Add((filePath, string.Join("\n", patchLines)));
            }

            return result;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — batch dispatch
        // ═══════════════════════════════════════════════════════════════════════

        private async Task<EditResult> DispatchBatchItem(
            BatchEditItem item, bool dryRun,
            string? changeReason, string? sessionId, CancellationToken ct)
        {
            if (item == null)                               throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.Path))       throw new ArgumentException("Batch item Path is required.");
            if (string.IsNullOrWhiteSpace(item.Operation))  throw new ArgumentException("Batch item Operation is required.");

            var opts = new EditOptions { DryRun = dryRun, ChangeReason = changeReason, SessionId = sessionId };

            switch (item.Operation.ToLowerInvariant())
            {
                case "write_file":
                    return await WriteFileAsync(new WriteFileRequest
                    {
                        Path = item.Path, Content = item.Content ?? string.Empty,
                        Encoding = item.Encoding!, Options = opts
                    }, ct);

                case "create_file":
                    return await CreateFileAsync(new CreateFileRequest
                    {
                        Path = item.Path, Content = item.Content ?? string.Empty,
                        Encoding = item.Encoding!, Options = opts
                    }, ct);

                case "delete_file":
                    return await DeleteFileAsync(new DeleteFileRequest
                    { Path = item.Path, Options = opts }, ct);

                case "append_file":
                    return await AppendFileAsync(new AppendFileRequest
                    { Path = item.Path, Content = item.Content ?? string.Empty, Options = opts }, ct);

                case "replace_text":
                    return await ReplaceTextAsync(new ReplaceTextRequest
                    {
                        Path = item.Path, OldText = item.OldText!, NewText = item.NewText ?? string.Empty,
                        IgnoreCase = item.IgnoreCase, Options = opts
                    }, ct);

                case "replace_lines":
                    return await ReplaceLinesAsync(new ReplaceLinesRequest
                    {
                        Path = item.Path, StartLine = item.StartLine, EndLine = item.EndLine,
                        NewContent = item.NewText ?? string.Empty, Options = opts
                    }, ct);

                case "patch_file":
                    return await PatchFileAsync(new PatchFileRequest
                    { Path = item.Path, Patch = item.Patch!, Options = opts }, ct);

                default:
                    throw new ArgumentException($"Unknown batch operation: '{item.Operation}'.");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — encoding / text utilities
        // ═══════════════════════════════════════════════════════════════════════

        private static Encoding GetEncoding(string name)
        {
            switch ((name ?? "utf-8").ToLowerInvariant())
            {
                case "utf-8-bom": return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                case "utf-16":    return Encoding.Unicode;
                case "ascii":     return Encoding.ASCII;
                default:          return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
        }

        /// <summary>Detects encoding by reading BOM bytes; falls back to UTF-8 without BOM.</summary>
        private static Encoding DetectFileEncoding(string absPath)
        {
            using (var fs = new FileStream(absPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bom  = new byte[4];
                int read = fs.Read(bom, 0, 4);
                if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
                if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;
            }
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        private static string NormalizeLineEndings(string content, string style, string? existingContent)
        {
            if (string.IsNullOrEmpty(style)
                || style.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                style = "lf";
                if (!string.IsNullOrEmpty(existingContent))
                {
                    if (existingContent.IndexOf("\r\n", StringComparison.Ordinal) >= 0) style = "crlf";
                    else if (existingContent.IndexOf('\r') >= 0)                         style = "cr";
                }
            }

            // Normalise to LF first, then re-apply target style.
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            switch (style.ToLowerInvariant())
            {
                case "crlf": return content.Replace("\n", "\r\n");
                case "cr":   return content.Replace("\n", "\r");
                default:     return content;   // lf
            }
        }

        private static int CountOccurrences(string haystack, string needle, StringComparison cmp)
        {
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, cmp)) >= 0)
            { count++; idx += needle.Length; }
            return count;
        }

        private static string ReplaceWithLimit(
            string content, string oldText, string newText,
            StringComparison cmp, int maxReplacements)
        {
            var sb      = new StringBuilder(content.Length);
            int index   = 0;
            int replaced = 0;

            while (maxReplacements <= 0 || replaced < maxReplacements)
            {
                int pos = content.IndexOf(oldText, index, cmp);
                if (pos < 0) break;
                sb.Append(content, index, pos - index);
                sb.Append(newText);
                index = pos + oldText.Length;
                replaced++;
            }

            sb.Append(content, index, content.Length - index);
            return sb.ToString();
        }

        private static string? Truncate(string? s, int max) =>
            s != null && s.Length > max ? s.Substring(0, max) + "…" : s;

        private static int CountDiffLines(string? diff, char op)
        {
            if (string.IsNullOrEmpty(diff)) return 0;
            return diff.Split('\n')
                       .Count(l => l.Length > 0 && l[0] == op
                                   && !l.StartsWith("---") && !l.StartsWith("+++"));
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — I/O helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static Task<string> ReadFileTextAsync(string absPath, CancellationToken ct) =>
            Task.Run(() => File.ReadAllText(absPath, Encoding.UTF8), ct);

        private static void EnsureParentDirectory(string absPath)
        {
            var dir = Path.GetDirectoryName(absPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — audit helpers
        // ═══════════════════════════════════════════════════════════════════════

        private Task Audit(EditOptions opts, string opName, string relPath,
                           AuditOutcome outcome, string? backupPath,
                           long elapsedMs, CancellationToken ct) =>
            _auditService.LogOperationAsync(
                requestId     : Guid.NewGuid().ToString("N"),
                sessionId     : opts?.SessionId,
                operationType : MapAuditType(opName),
                operationName : opName,
                clientIp      : "service",
                targetPath    : relPath,
                outcome       : outcome,
                errorCode     : null,
                description   : $"{opName} on {relPath}",
                elapsedMs     : elapsedMs,
                changeReason  : opts?.ChangeReason,
                backupPath    : backupPath,
                ct            : ct);

        private static AuditOperationType MapAuditType(string opName)
        {
            switch (opName)
            {
                case "write_file":       return AuditOperationType.WriteFile;
                case "create_file":      return AuditOperationType.CreateFile;
                case "create_directory": return AuditOperationType.CreateDirectory;
                case "rename_path":
                case "move_path":        return AuditOperationType.RenameMove;
                case "delete_file":      return AuditOperationType.DeleteFile;
                case "delete_directory": return AuditOperationType.DeleteDirectory;
                case "append_file":      return AuditOperationType.AppendFile;
                case "replace_text":     return AuditOperationType.ReplaceText;
                case "replace_lines":    return AuditOperationType.ReplaceLines;
                case "patch_file":       return AuditOperationType.PatchFile;
                default:                 return AuditOperationType.Unknown;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Private — result factories
        // ═══════════════════════════════════════════════════════════════════════

        private static EditResult DryRunResult(
            string relPath, string op, string? diff, int affected) =>
            new EditResult
            {
                Success       = true,
                RelativePath  = relPath,
                Operation     = op,
                WasDryRun     = true,
                Diff          = diff,
                AffectedCount = affected
            };

        private static EditResult SuccessResult(
            string relPath, string op, string? diff, int affected,
            string? backupPath, string? rollbackToken) =>
            new EditResult
            {
                Success       = true,
                RelativePath  = relPath,
                Operation     = op,
                BackupPath    = backupPath,
                RollbackToken = rollbackToken,
                Diff          = diff,
                AffectedCount = affected
            };
    }
}
