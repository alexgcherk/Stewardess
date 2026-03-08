using System;
using System.IO;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;
using StewardessMCPServive.Tests.Helpers;
using Xunit;

namespace StewardessMCPServive.Tests.Infrastructure
{
    /// <summary>
    /// Unit tests for <see cref="PathValidator"/>.
    /// These cover the most security-critical component in the service.
    /// </summary>
    public sealed class PathValidatorTests : IDisposable
    {
        private readonly TempRepository _repo;
        private readonly PathValidator  _validator;

        public PathValidatorTests()
        {
            _repo      = new TempRepository();
            _validator = BuildValidator(_repo.Root);
        }

        public void Dispose() => _repo.Dispose();

        // ── Validate (read) ──────────────────────────────────────────────────────

        [Fact]
        public void Validate_ValidRelativePath_IsValid()
        {
            _repo.CreateFile(@"src\Class1.cs", "content");
            var result = _validator.Validate(@"src\Class1.cs", out _);
            Assert.True(result.IsValid, result.ErrorMessage);
        }

        [Fact]
        public void Validate_EmptyPath_IsValid_MapToRoot()
        {
            var result = _validator.Validate("", out var abs);
            Assert.True(result.IsValid);
            Assert.Equal(_repo.Root.TrimEnd('\\', '/'),
                         abs.TrimEnd('\\', '/'),
                         StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(@"..\outside.txt")]
        [InlineData(@"..\..\windows\system32\cmd.exe")]
        [InlineData(@"sub\..\..\..\escape.txt")]
        public void Validate_PathTraversal_IsInvalid(string relativePath)
        {
            var result = _validator.Validate(relativePath, out _);
            Assert.False(result.IsValid);
            Assert.Equal(ErrorCodes.PathTraversal, result.ErrorCode);
        }

        [Fact]
        public void Validate_NullByte_IsInvalid()
        {
            var result = _validator.Validate("file\0name.cs", out _);
            Assert.False(result.IsValid);
        }

        [Fact]
        public void Validate_AbsolutePathWithDriveColon_IsInvalid()
        {
            // Drive-letter absolute paths are now rejected: only relative paths are accepted.
            // This prevents the drive-relative path traversal (e.g. "C:foo") bypass.
            var abs    = Path.Combine(_repo.Root, "src", "Class1.cs");  // contains "C:"
            var result = _validator.Validate(abs, out _);
            Assert.False(result.IsValid);
            Assert.Equal(ErrorCodes.InvalidPath, result.ErrorCode);
        }

        [Fact]
        public void Validate_AbsolutePathOutsideRoot_IsInvalid()
        {
            var result = _validator.Validate(@"C:\Windows\System32\cmd.exe", out _);
            Assert.False(result.IsValid);
            // Drive-letter paths are caught by the colon check (InvalidPath) before
            // reaching the sandbox check (PathTraversal).
            Assert.Equal(ErrorCodes.InvalidPath, result.ErrorCode);
        }

        // ── ValidateWrite ────────────────────────────────────────────────────────

        [Fact]
        public void ValidateWrite_BlockedExtension_IsInvalid()
        {
            var result = _validator.ValidateWrite(@"src\output.exe", out _);
            Assert.False(result.IsValid);
        }

        [Fact]
        public void ValidateWrite_AllowedExtension_IsValid()
        {
            var result = _validator.ValidateWrite(@"src\Class1.cs", out var abs);
            Assert.True(result.IsValid, result.ErrorMessage);
            Assert.False(string.IsNullOrEmpty(abs));
        }

        // ── Blocked folders ──────────────────────────────────────────────────────

        [Theory]
        [InlineData(@"bin\Debug\MyApp.dll")]
        [InlineData(@"obj\Release\temp.txt")]
        [InlineData(@".git\COMMIT_EDITMSG")]
        [InlineData(@".vs\settings.json")]
        public void Validate_InsideBlockedFolder_IsInvalid(string relativePath)
        {
            // ValidateRead enforces folder + extension blocking; Validate() is sandbox-only.
            var result = _validator.ValidateRead(relativePath, out _);
            Assert.False(result.IsValid);
            Assert.Equal(ErrorCodes.BlockedFolder, result.ErrorCode);
        }

        [Fact]
        public void Validate_FolderNamePrefixNotBlocked()
        {
            // "binary" starts with "bin" but is not the exact segment "bin"
            _repo.CreateFile(@"binary\output.txt", "data");
            var result = _validator.Validate(@"binary\output.txt", out _);
            Assert.True(result.IsValid, result.ErrorMessage);
        }

        // ── ToRelativePath / ToAbsolutePath ──────────────────────────────────────

        [Fact]
        public void ToRelativePath_ReturnsPathSegments()
        {
            var abs = Path.Combine(_repo.Root, "src", "Class1.cs");
            var rel = _validator.ToRelativePath(abs);
            // On Windows the separator is backslash; normalise for assertion.
            var normalised = rel.Replace(Path.DirectorySeparatorChar, '/');
            Assert.Equal("src/Class1.cs", normalised);
        }

        [Fact]
        public void ToAbsolutePath_RoundTrip()
        {
            var original = @"src\Foo.cs";
            var abs      = _validator.ToAbsolutePath(original);
            var rel      = _validator.ToRelativePath(abs);
            var normalised = rel.Replace(Path.DirectorySeparatorChar, '/');
            Assert.Equal("src/Foo.cs", normalised);
        }

        /// <summary>
        /// Regression test for: "startIndex cannot be larger than length of string".
        /// _repositoryRoot is stored with a trailing separator; Path.GetFullPath of the
        /// root itself has no trailing separator.  The Substring call must not overshoot.
        /// </summary>
        [Fact]
        public void ToRelativePath_RepositoryRoot_ReturnsEmptyString()
        {
            // Passing the repo root itself (no trailing separator).
            var rel = _validator.ToRelativePath(_repo.Root);
            Assert.Equal(string.Empty, rel);
        }

        [Fact]
        public void ToRelativePath_RepositoryRootWithTrailingSeparator_ReturnsEmptyString()
        {
            // Passing the repo root with a trailing separator — same logical path.
            var rel = _validator.ToRelativePath(_repo.Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar);
            Assert.Equal(string.Empty, rel);
        }

        [Fact]
        public void ToRelativePath_NullOrEmpty_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, _validator.ToRelativePath(null));
            Assert.Equal(string.Empty, _validator.ToRelativePath(string.Empty));
        }

        // ── Security regression: drive-relative path traversal (S01) ────────────

        [Theory]
        [InlineData("C:foo")]                    // drive-relative, no slash
        [InlineData("C:Windows\\System32")]      // drive-relative with path
        [InlineData("D:secret.txt")]             // different drive
        [InlineData("c:lowercase")]              // lowercase drive
        public void Validate_DriveRelativePath_IsInvalid(string path)
        {
            // "C:foo" passes TrimStart('/','\') unchanged but Path.Combine(root, "C:foo")
            // returns "C:foo" verbatim, and Path.GetFullPath resolves it vs the *current*
            // directory of drive C — not the repo root.  Must be rejected early.
            var result = _validator.Validate(path, out _);
            Assert.False(result.IsValid);
            Assert.Equal(ErrorCodes.InvalidPath, result.ErrorCode);
        }

        // ── Security regression: backup and internal folders blocked (S04) ────────

        [Theory]
        [InlineData(@".mcp_backups\src\original.cs")]
        [InlineData(@".mcp_backups\Class1.cs.bak")]
        [InlineData(@".mcp\audit.log")]
        public void ValidateRead_InternalServiceFolders_AreBlocked(string path)
        {
            // Backup and service directories must be blocked so callers cannot
            // read backup copies of edited files through the read_file API.
            var validator = BuildValidatorWithDefaults(_repo.Root);
            var result    = validator.ValidateRead(path, out _);
            Assert.False(result.IsValid);
            Assert.Equal(ErrorCodes.BlockedFolder, result.ErrorCode);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static PathValidator BuildValidator(string root)
        {
            var settings = McpServiceSettings.CreateForTesting(
                repositoryRoot    : root,
                readOnly          : false,
                blockedFolders    : new[] { ".git", "bin", "obj", ".vs" },
                blockedExtensions : new[] { ".exe", ".dll", ".pdb" });
            return new PathValidator(settings);
        }

        private static PathValidator BuildValidatorWithDefaults(string root)
        {
            // Uses the same default blocked-folders list as production McpServiceSettings.
            var settings = McpServiceSettings.CreateForTesting(
                repositoryRoot    : root,
                blockedFolders    : new[] { ".git", "bin", "obj", ".mcp_backups", ".mcp" },
                blockedExtensions : new[] { ".exe", ".dll" });
            return new PathValidator(settings);
        }
    }
}
