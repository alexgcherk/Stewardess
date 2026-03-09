using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StewardessMCPService.Configuration;
using StewardessMCPService.Models;

namespace StewardessMCPService.Infrastructure
{
    /// <summary>
    /// Security-critical helper that validates and normalises file paths.
    ///
    /// All file access MUST go through this class.  The implementation:
    ///   1. Rejects null/empty paths.
    ///   2. Strips and normalises the path with <see cref="Path.GetFullPath"/>.
    ///   3. Ensures the normalised absolute path starts with RepositoryRoot.
    ///   4. Checks the path against blocked-folder and blocked-extension lists.
    ///
    /// No method in this class performs any disk I/O.
    /// </summary>
    public sealed class PathValidator
    {
        private readonly string _repositoryRoot;       // normalised, ends with separator
        private readonly IReadOnlyCollection<string> _blockedFolders;
        private readonly IReadOnlyCollection<string> _blockedExtensions;
        private readonly IReadOnlyCollection<string> _allowedExtensions;

        /// <summary>Initialises a new instance of <see cref="PathValidator"/>.</summary>
        public PathValidator(McpServiceSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.RepositoryRoot))
                throw new InvalidOperationException(
                    "Mcp:RepositoryRoot is not configured. Please set it in Web.config.");

            // Normalise to an absolute path with a trailing separator so prefix checks work.
            _repositoryRoot    = EnsureTrailingSeparator(Path.GetFullPath(settings.RepositoryRoot));
            _blockedFolders    = settings.BlockedFolders;
            _blockedExtensions = settings.BlockedExtensions;
            _allowedExtensions = settings.AllowedExtensions;
        }

        // ── Public property ──────────────────────────────────────────────────────

        /// <summary>The normalised, trailing-separator absolute path of the repository root.</summary>
        public string RepositoryRoot => _repositoryRoot;

        // ── Core validation ──────────────────────────────────────────────────────

        /// <summary>
        /// Validates that a caller-supplied relative path resolves to a location
        /// inside the repository root.
        /// </summary>
        /// <param name="relativePath">
        /// Path supplied by the caller.  May use forward or back slashes.
        /// Leading slashes are stripped.  Must not be null.
        /// </param>
        /// <param name="absolutePath">
        /// On success, the fully resolved absolute path (no trailing separator).
        /// On failure, an empty string.
        /// </param>
        /// <returns>A <see cref="ValidationResult"/> describing the outcome.</returns>
        public ValidationResult Validate(string relativePath, out string absolutePath)
        {
            absolutePath = string.Empty;

            if (relativePath == null)
                return ValidationResult.Fail(ErrorCodes.InvalidPath, "Path must not be null.");

            // Strip leading slash/backslash so Path.Combine works correctly.
            var sanitised = relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);

            // Catch null-byte injection attempts.
            if (sanitised.IndexOf('\0') >= 0)
                return ValidationResult.Fail(ErrorCodes.InvalidPath, "Path contains invalid characters.");

            // Reject drive-relative paths such as "C:foo" (colon present but not an absolute path).
            // On Windows, Path.Combine(root, "C:foo") returns "C:foo" verbatim, and
            // Path.GetFullPath("C:foo") resolves relative to the current directory of drive C —
            // not relative to RepositoryRoot — which would bypass the sandbox.
            if (sanitised.IndexOf(':') >= 0)
                return ValidationResult.Fail(ErrorCodes.InvalidPath,
                    "Path contains invalid characters (drive-letter specifiers are not permitted).");

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(_repositoryRoot, sanitised));
            }
            catch (Exception ex)
            {
                return ValidationResult.Fail(ErrorCodes.InvalidPath, $"Path could not be resolved: {ex.Message}");
            }

            // Sandbox check: the resolved path must be inside (or equal to) the repository root.
            if (!IsInsideRoot(candidate))
                return ValidationResult.Fail(
                    ErrorCodes.PathTraversal,
                    "The resolved path is outside the repository root.  Path traversal is not permitted.");

            absolutePath = candidate;
            return ValidationResult.Ok();
        }

        /// <summary>
        /// Validates the path for read access.  In addition to the sandbox check,
        /// verifies that no path segment is in the blocked-folders list and that
        /// the file extension is not blocked.
        /// </summary>
        public ValidationResult ValidateRead(string relativePath, out string absolutePath)
        {
            var baseResult = Validate(relativePath, out absolutePath);
            if (!baseResult.IsValid) return baseResult;

            return CheckFolderAndExtension(absolutePath, requireWriteExtension: false);
        }

        /// <summary>
        /// Validates the path for write access.  Same checks as ReadPath plus a
        /// blocked-extension enforcement that is slightly stricter (no overwriting
        /// compiled artifacts).
        /// </summary>
        public ValidationResult ValidateWrite(string relativePath, out string absolutePath)
        {
            var baseResult = Validate(relativePath, out absolutePath);
            if (!baseResult.IsValid) return baseResult;

            return CheckFolderAndExtension(absolutePath, requireWriteExtension: true);
        }

        // ── Conversion helpers ───────────────────────────────────────────────────

        /// <summary>Converts a server-side absolute path to a repository-relative path.</summary>
        public string ToRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return string.Empty;

            var normalised = Path.GetFullPath(absolutePath);
            if (!IsInsideRoot(normalised)) return string.Empty;

            // _repositoryRoot has a trailing separator; normalised does not.
            // When the path IS the repository root, the relative path is empty.
            if (normalised.Length < _repositoryRoot.Length)
                return string.Empty;

            var rel = normalised.Substring(_repositoryRoot.Length);
            return rel.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// Converts a relative path to an absolute path without performing any
        /// security validation.  Use only after <see cref="Validate"/> has already passed.
        /// </summary>
        public string ToAbsolutePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return _repositoryRoot.TrimEnd(Path.DirectorySeparatorChar);

            var sanitised = relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(_repositoryRoot, sanitised));
        }

        // ── Folder / extension checks ────────────────────────────────────────────

        /// <summary>
        /// Returns true when any segment of <paramref name="absolutePath"/>
        /// between the repository root and the leaf matches a blocked-folder name.
        /// </summary>
        public bool IsFolderBlocked(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return false;

            // Walk the path segments that live below the repository root.
            var relative = ToRelativePath(absolutePath);
            if (string.IsNullOrEmpty(relative)) return false;

            var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                                          StringSplitOptions.RemoveEmptyEntries);

            return segments.Any(seg => _blockedFolders.Contains(seg));
        }

        /// <summary>
        /// Returns true when the file extension is in the blocked list,
        /// or when the allowed list is non-empty and the extension is absent from it.
        /// </summary>
        public bool IsExtensionBlocked(string absolutePath)
        {
            var ext = Path.GetExtension(absolutePath);
            if (string.IsNullOrEmpty(ext)) return false;

            if (_blockedExtensions.Contains(ext)) return true;

            // If an explicit allow-list exists the extension must be in it.
            if (_allowedExtensions != null && _allowedExtensions.Count > 0)
                return !_allowedExtensions.Contains(ext);

            return false;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private bool IsInsideRoot(string absolutePath)
        {
            // Ensure a trailing separator so "C:\repos\abc" does not match "C:\repos\abcXyz".
            return absolutePath.StartsWith(_repositoryRoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                       absolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                           Path.DirectorySeparatorChar,
                       _repositoryRoot,
                       StringComparison.OrdinalIgnoreCase);
        }

        private ValidationResult CheckFolderAndExtension(string absolutePath, bool requireWriteExtension)
        {
            if (IsFolderBlocked(absolutePath))
                return ValidationResult.Fail(
                    ErrorCodes.BlockedFolder,
                    "The path is inside a blocked folder.");

            if (requireWriteExtension && IsExtensionBlocked(absolutePath))
                return ValidationResult.Fail(
                    ErrorCodes.BlockedExtension,
                    "The file extension is not permitted for this operation.");

            // For reads, only block explicitly blocked extensions.
            if (!requireWriteExtension)
            {
                var ext = Path.GetExtension(absolutePath);
                if (!string.IsNullOrEmpty(ext) && _blockedExtensions.Contains(ext))
                    return ValidationResult.Fail(
                        ErrorCodes.BlockedExtension,
                        "The file extension is blocked.");
            }

            return ValidationResult.Ok();
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Validation result
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Lightweight result type returned by path validation methods.</summary>
    public sealed class ValidationResult
    {
        /// <summary>True when the path passed all validation checks.</summary>
        public bool IsValid { get; private set; }
        /// <summary>Machine-readable error code when validation failed; null on success.</summary>
        public string ErrorCode { get; private set; }
        /// <summary>Human-readable error message when validation failed; null on success.</summary>
        public string ErrorMessage { get; private set; }

        private ValidationResult() { }

        /// <summary>Creates a passing validation result.</summary>
        public static ValidationResult Ok() =>
            new ValidationResult { IsValid = true };

        /// <summary>Creates a failing validation result with the given error code and message.</summary>
        public static ValidationResult Fail(string code, string message) =>
            new ValidationResult { IsValid = false, ErrorCode = code, ErrorMessage = message };

        /// <summary>Implicit bool conversion so results can be used directly in if statements.</summary>
        public static implicit operator bool(ValidationResult r) => r.IsValid;
    }
}
