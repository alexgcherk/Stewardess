using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using StewardessMCPService.Tests.Helpers;
using Xunit;

namespace StewardessMCPService.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="EditService"/>.
    /// Covers all mutating operations: write, create, delete, patch, diff,
    /// batch edits, rollback, preview, dry-run, and path-traversal rejection.
    /// </summary>
    public sealed class EditServiceTests : IDisposable
    {
        private readonly TempRepository _repo;
        private readonly EditService    _svc;
        private readonly EditService    _roSvc;   // read-only mode

        public EditServiceTests()
        {
            _repo  = new TempRepository();
            _repo.CreateSampleCsStructure();
            _svc   = Build(_repo.Root, readOnly: false);
            _roSvc = Build(_repo.Root, readOnly: true);
        }

        public void Dispose() => _repo.Dispose();

        // ── WriteFile ────────────────────────────────────────────────────────────

        [Fact]
        public async Task WriteFile_NewFile_CreatesFile()
        {
            var result = await _svc.WriteFileAsync(
                new WriteFileRequest { Path = "newfile.cs", Content = "// hello" });
            Assert.True(result.Success);
            Assert.False(result.WasDryRun);
            Assert.Equal("// hello", File.ReadAllText(_repo.Abs("newfile.cs")));
        }

        [Fact]
        public async Task WriteFile_ExistingFile_OverwritesContent()
        {
            _repo.CreateFile("over.cs", "original");
            await _svc.WriteFileAsync(new WriteFileRequest { Path = "over.cs", Content = "replaced" });
            Assert.Equal("replaced", File.ReadAllText(_repo.Abs("over.cs")));
        }

        [Fact]
        public async Task WriteFile_DryRun_DoesNotChangeFile()
        {
            _repo.CreateFile("dry.cs", "original");
            var result = await _svc.WriteFileAsync(new WriteFileRequest
            {
                Path    = "dry.cs",
                Content = "changed",
                Options = new EditOptions { DryRun = true }
            });
            Assert.True(result.Success);
            Assert.True(result.WasDryRun);
            Assert.Equal("original", File.ReadAllText(_repo.Abs("dry.cs")));
            Assert.False(string.IsNullOrEmpty(result.Diff));
        }

        [Fact]
        public async Task WriteFile_WithBackup_CreatesBackupAndRollbackToken()
        {
            _repo.CreateFile("backup.cs", "original content");
            var result = await _svc.WriteFileAsync(new WriteFileRequest
            {
                Path    = "backup.cs",
                Content = "new content",
                Options = new EditOptions { CreateBackup = true }
            });
            Assert.False(string.IsNullOrEmpty(result.BackupPath));
            Assert.False(string.IsNullOrEmpty(result.RollbackToken));
            Assert.Equal("new content", File.ReadAllText(_repo.Abs("backup.cs")));
        }

        [Fact]
        public async Task WriteFile_ReadOnlyMode_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _roSvc.WriteFileAsync(new WriteFileRequest { Path = "x.cs", Content = "" }));
        }

        [Fact]
        public async Task WriteFile_PathTraversal_Throws()
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _svc.WriteFileAsync(new WriteFileRequest { Path = @"..\..\evil.txt", Content = "x" }));
        }

        // ── CreateFile ───────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateFile_NewFile_CreatesIt()
        {
            var result = await _svc.CreateFileAsync(
                new CreateFileRequest { Path = "brand_new.cs", Content = "// new" });
            Assert.True(result.Success);
            Assert.True(File.Exists(_repo.Abs("brand_new.cs")));
        }

        [Fact]
        public async Task CreateFile_ExistsNoOverwrite_ThrowsIOException()
        {
            _repo.CreateFile("exists.cs", "data");
            await Assert.ThrowsAsync<IOException>(() =>
                _svc.CreateFileAsync(new CreateFileRequest { Path = "exists.cs", Content = "x", Overwrite = false }));
        }

        [Fact]
        public async Task CreateFile_ExistsWithOverwrite_Succeeds()
        {
            _repo.CreateFile("ovr.cs", "old");
            var result = await _svc.CreateFileAsync(
                new CreateFileRequest { Path = "ovr.cs", Content = "new", Overwrite = true });
            Assert.True(result.Success);
            Assert.Equal("new", File.ReadAllText(_repo.Abs("ovr.cs")));
        }

        // ── CreateDirectory ──────────────────────────────────────────────────────

        [Fact]
        public async Task CreateDirectory_NewDir_CreatesIt()
        {
            var result = await _svc.CreateDirectoryAsync(
                new CreateDirectoryRequest { Path = "newdir", CreateParents = true });
            Assert.True(result.Success);
            Assert.True(Directory.Exists(_repo.Abs("newdir")));
        }

        [Fact]
        public async Task CreateDirectory_Nested_CreatesParents()
        {
            var result = await _svc.CreateDirectoryAsync(
                new CreateDirectoryRequest { Path = @"deep\nested\dir", CreateParents = true });
            Assert.True(result.Success);
            Assert.True(Directory.Exists(_repo.Abs(@"deep\nested\dir")));
        }

        // ── RenamePath ───────────────────────────────────────────────────────────

        [Fact]
        public async Task RenamePath_File_RenamesCorrectly()
        {
            _repo.CreateFile("oldname.cs", "content");
            var result = await _svc.RenamePathAsync(
                new RenamePathRequest { Path = "oldname.cs", NewName = "newname.cs" });
            Assert.True(result.Success);
            Assert.False(File.Exists(_repo.Abs("oldname.cs")));
            Assert.True(File.Exists(_repo.Abs("newname.cs")));
        }

        // ── MovePath ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task MovePath_File_MovesCorrectly()
        {
            _repo.CreateFile("tomove.cs", "data");
            Directory.CreateDirectory(_repo.Abs("dest"));
            var result = await _svc.MovePathAsync(new MovePathRequest
            {
                SourcePath      = "tomove.cs",
                DestinationPath = @"dest\tomove.cs"
            });
            Assert.True(result.Success);
            Assert.False(File.Exists(_repo.Abs("tomove.cs")));
            Assert.True(File.Exists(_repo.Abs(@"dest\tomove.cs")));
        }

        // ── DeleteFile ───────────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteFile_ExistingFile_DeletesIt()
        {
            _repo.CreateFile("todelete.cs", "x");
            var result = await _svc.DeleteFileAsync(new DeleteFileRequest { Path = "todelete.cs" });
            Assert.True(result.Success);
            Assert.False(File.Exists(_repo.Abs("todelete.cs")));
        }

        [Fact]
        public async Task DeleteFile_CreatesBackupAndRollbackToken()
        {
            _repo.CreateFile("willdelete.cs", "saved content");
            var result = await _svc.DeleteFileAsync(
                new DeleteFileRequest { Path = "willdelete.cs",
                                        Options = new EditOptions { CreateBackup = true } });
            Assert.False(string.IsNullOrEmpty(result.RollbackToken));
            Assert.False(string.IsNullOrEmpty(result.BackupPath));
        }

        [Fact]
        public async Task DeleteFile_NotFound_ThrowsFileNotFoundException()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _svc.DeleteFileAsync(new DeleteFileRequest { Path = "ghost.cs" }));
        }

        // ── DeleteDirectory ──────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteDirectory_Empty_Deletes()
        {
            Directory.CreateDirectory(_repo.Abs("emptydir"));
            var result = await _svc.DeleteDirectoryAsync(
                new DeleteDirectoryRequest { Path = "emptydir" });
            Assert.True(result.Success);
            Assert.False(Directory.Exists(_repo.Abs("emptydir")));
        }

        [Fact]
        public async Task DeleteDirectory_NonEmptyWithoutRecursive_Throws()
        {
            _repo.CreateFile(@"nonempty\file.cs", "data");
            await Assert.ThrowsAsync<IOException>(() =>
                _svc.DeleteDirectoryAsync(
                    new DeleteDirectoryRequest { Path = "nonempty", Recursive = false }));
        }

        [Fact]
        public async Task DeleteDirectory_NonEmptyRecursive_Deletes()
        {
            _repo.CreateFile(@"todel2\file.cs", "data");
            var result = await _svc.DeleteDirectoryAsync(
                new DeleteDirectoryRequest { Path = "todel2", Recursive = true });
            Assert.True(result.Success);
            Assert.False(Directory.Exists(_repo.Abs("todel2")));
        }

        // ── AppendFile ───────────────────────────────────────────────────────────

        [Fact]
        public async Task AppendFile_AddsContent()
        {
            _repo.CreateFile("append.cs", "line1");
            await _svc.AppendFileAsync(new AppendFileRequest
            { Path = "append.cs", Content = "line2", EnsureNewLine = true });
            var text = File.ReadAllText(_repo.Abs("append.cs"));
            Assert.Contains("line1", text);
            Assert.Contains("line2", text);
        }

        // ── ReplaceText ──────────────────────────────────────────────────────────

        [Fact]
        public async Task ReplaceText_ReplacesOccurrences()
        {
            _repo.CreateFile("replace.cs", "foo bar foo");
            var result = await _svc.ReplaceTextAsync(new ReplaceTextRequest
            { Path = "replace.cs", OldText = "foo", NewText = "baz" });
            Assert.Equal(2, result.AffectedCount);
            Assert.Equal("baz bar baz", File.ReadAllText(_repo.Abs("replace.cs")));
        }

        [Fact]
        public async Task ReplaceText_MaxReplacements_LimitsCount()
        {
            _repo.CreateFile("maxrep.cs", "a a a a");
            var result = await _svc.ReplaceTextAsync(new ReplaceTextRequest
            { Path = "maxrep.cs", OldText = "a", NewText = "b", MaxReplacements = 2 });
            Assert.Equal(2, result.AffectedCount);
            Assert.Equal("b b a a", File.ReadAllText(_repo.Abs("maxrep.cs")));
        }

        [Fact]
        public async Task ReplaceText_OldTextNotFound_Throws()
        {
            _repo.CreateFile("notfound.cs", "hello");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _svc.ReplaceTextAsync(new ReplaceTextRequest
                { Path = "notfound.cs", OldText = "MISSING", NewText = "x" }));
        }

        // ── ReplaceLines ─────────────────────────────────────────────────────────

        [Fact]
        public async Task ReplaceLines_ValidRange_ReplacesLines()
        {
            _repo.CreateFile("rl.cs", "line1\nline2\nline3\nline4");
            var result = await _svc.ReplaceLinesAsync(new ReplaceLinesRequest
            {
                Path       = "rl.cs",
                StartLine  = 2,
                EndLine    = 3,
                NewContent = "REPLACED"
            });
            Assert.True(result.Success);
            Assert.Equal(2, result.AffectedCount);
            var text = File.ReadAllText(_repo.Abs("rl.cs"));
            Assert.Contains("REPLACED", text);
            Assert.Contains("line1",    text);
            Assert.Contains("line4",    text);
            Assert.DoesNotContain("line2", text);
            Assert.DoesNotContain("line3", text);
        }

        // ── PatchFile ────────────────────────────────────────────────────────────

        [Fact]
        public async Task PatchFile_ValidPatch_AppliesCorrectly()
        {
            const string original = "line1\nline2\nline3\n";
            _repo.CreateFile("topatch.cs", original);

            const string patch = @"--- a/topatch.cs
+++ b/topatch.cs
@@ -1,3 +1,3 @@
 line1
-line2
+line2_patched
 line3
";
            var result = await _svc.PatchFileAsync(new PatchFileRequest
            { Path = "topatch.cs", Patch = patch });
            Assert.True(result.Success);
            Assert.Contains("line2_patched", File.ReadAllText(_repo.Abs("topatch.cs")));
        }

        [Fact]
        public async Task PatchFile_InvalidPatch_ThrowsInvalidOperation()
        {
            _repo.CreateFile("badpatch.cs", "hello\nworld\n");
            const string badPatch = @"--- a/badpatch.cs
+++ b/badpatch.cs
@@ -1,2 +1,2 @@
 WRONG_CONTEXT_LINE
-world
+earth
";
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _svc.PatchFileAsync(new PatchFileRequest
                { Path = "badpatch.cs", Patch = badPatch, FuzzFactor = 0 }));
        }

        // ── PatchFile — path fallback (bare filename → embedded patch header) ────

        /// <summary>
        /// Regression: AI agents often pass only the bare filename in "path" while the
        /// patch body contains the full relative path in "*** Update File:" format.
        /// The service must fall back to the embedded path rather than returning 404.
        /// </summary>
        [Fact]
        public async Task PatchFile_BareFilename_UpdateFileHeader_ResolvesFullPath()
        {
            _repo.CreateFile(@"sub\dir\Target.cs", "original\n");

            const string patch =
                "*** Begin Patch\n" +
                "*** Update File: sub/dir/Target.cs\n" +
                "@@ -1,1 +1,1 @@\n" +
                "-original\n" +
                "+replaced\n";

            // Caller only supplies the bare filename — the service must find the file
            // via the "*** Update File:" line inside the patch.
            var result = await _svc.PatchFileAsync(new PatchFileRequest
            { Path = "Target.cs", Patch = patch });

            Assert.True(result.Success);
            Assert.Contains("replaced", File.ReadAllText(_repo.Abs(@"sub\dir\Target.cs")));
        }

        [Fact]
        public async Task PatchFile_BareFilename_PlusPlusHeader_ResolvesFullPath()
        {
            _repo.CreateFile(@"deep\nested\Util.cs", "alpha\n");

            const string patch = @"--- a/deep/nested/Util.cs
+++ b/deep/nested/Util.cs
@@ -1,1 +1,1 @@
-alpha
+beta
";
            var result = await _svc.PatchFileAsync(new PatchFileRequest
            { Path = "Util.cs", Patch = patch });

            Assert.True(result.Success);
            Assert.Contains("beta", File.ReadAllText(_repo.Abs(@"deep\nested\Util.cs")));
        }

        [Fact]
        public async Task PatchFile_BareFilename_NoMatchingFile_ThrowsFileNotFound()
        {
            // Neither "path" nor embedded patch header point to an existing file.
            const string patch =
                "*** Begin Patch\n" +
                "*** Update File: does/not/Exist.cs\n" +
                "@@ -1,1 +1,1 @@\n" +
                "-old\n" +
                "+new\n";

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _svc.PatchFileAsync(new PatchFileRequest
                { Path = "Exist.cs", Patch = patch }));
        }

        [Fact]
        public async Task PatchFile_ExplicitCorrectPath_DoesNotRequireFallback()
        {
            _repo.CreateFile(@"pkg\Code.cs", "before\n");

            const string patch =
                "*** Begin Patch\n" +
                "*** Update File: pkg/Code.cs\n" +
                "@@ -1,1 +1,1 @@\n" +
                "-before\n" +
                "+after\n";

            // Full relative path given — fallback not needed, must still work.
            var result = await _svc.PatchFileAsync(new PatchFileRequest
            { Path = @"pkg\Code.cs", Patch = patch });

            Assert.True(result.Success);
            Assert.Contains("after", File.ReadAllText(_repo.Abs(@"pkg\Code.cs")));
        }

        // ── PatchFile — tier-3 fallback: repo-wide filename search ───────────────

        /// <summary>
        /// When neither the direct path nor the patch header resolves to a file,
        /// the service searches the repository for a file with the same bare name.
        /// If exactly one match exists it must be used successfully.
        /// </summary>
        [Fact]
        public async Task PatchFile_BareFilename_SingleMatchInRepo_ResolvedViaSearch()
        {
            // File lives in a subdirectory; caller passes only the bare name with no
            // patch header that contains the full path.
            _repo.CreateFile(@"somewhere\deep\Lonely.cs", "before\n");

            const string patch =
                "@@ -1,1 +1,1 @@\n" +
                "-before\n" +
                "+after\n";

            var result = await _svc.PatchFileAsync(new PatchFileRequest
            { Path = "Lonely.cs", Patch = patch });

            Assert.True(result.Success);
            Assert.Contains("after", File.ReadAllText(_repo.Abs(@"somewhere\deep\Lonely.cs")));
        }

        /// <summary>
        /// When a bare filename matches multiple files in the repository the service
        /// must throw an <see cref="InvalidOperationException"/> listing the candidates
        /// so the caller can supply a fully-qualified path.
        /// </summary>
        [Fact]
        public async Task PatchFile_BareFilename_MultipleMatchesInRepo_ThrowsAmbiguityError()
        {
            // Two files with the same name in different directories.
            _repo.CreateFile(@"module_a\Shared.cs", "version_a\n");
            _repo.CreateFile(@"module_b\Shared.cs", "version_b\n");

            const string patch =
                "@@ -1,1 +1,1 @@\n" +
                "-version_a\n" +
                "+replaced\n";

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _svc.PatchFileAsync(new PatchFileRequest
                { Path = "Shared.cs", Patch = patch }));

            // Error message must call out the ambiguity and list both candidates.
            Assert.Contains("Ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Shared.cs", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("module_a", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("module_b", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ApplyDiff_MultiFile_PatchesBothFiles()
        {
            _repo.CreateFile("alpha.cs", "alpha_original\n");
            _repo.CreateFile("beta.cs",  "beta_original\n");

            const string multiDiff = @"--- a/alpha.cs
+++ b/alpha.cs
@@ -1,1 +1,1 @@
-alpha_original
+alpha_patched
--- a/beta.cs
+++ b/beta.cs
@@ -1,1 +1,1 @@
-beta_original
+beta_patched
";
            var result = await _svc.ApplyDiffAsync(new ApplyDiffRequest { Diff = multiDiff });
            Assert.True(result.Success);
            Assert.Equal(2, result.SucceededCount);
            Assert.Contains("alpha_patched", File.ReadAllText(_repo.Abs("alpha.cs")));
            Assert.Contains("beta_patched",  File.ReadAllText(_repo.Abs("beta.cs")));
        }

        // ── BatchEdits ───────────────────────────────────────────────────────────

        [Fact]
        public async Task BatchEdits_AllSucceed_ReturnsSuccess()
        {
            _repo.CreateFile("b1.cs", "hello");
            _repo.CreateFile("b2.cs", "world");

            var result = await _svc.ApplyBatchEditsAsync(new BatchEditRequest
            {
                Edits = {
                    new BatchEditItem { Operation = "replace_text", Path = "b1.cs",
                                        OldText = "hello", NewText = "hi" },
                    new BatchEditItem { Operation = "replace_text", Path = "b2.cs",
                                        OldText = "world", NewText = "earth" }
                }
            });

            Assert.True(result.Success);
            Assert.Equal(2, result.SucceededCount);
            Assert.Equal(0, result.FailedCount);
        }

        [Fact]
        public async Task BatchEdits_OneFails_RollsBackPreviousOps()
        {
            _repo.CreateFile("rb1.cs", "original1");

            var result = await _svc.ApplyBatchEditsAsync(new BatchEditRequest
            {
                Edits = {
                    new BatchEditItem { Operation = "replace_text", Path = "rb1.cs",
                                        OldText = "original1", NewText = "changed1" },
                    // This operation will fail (file doesn't exist).
                    new BatchEditItem { Operation = "replace_text", Path = "nonexistent.cs",
                                        OldText = "x", NewText = "y" }
                }
            });

            Assert.False(result.Success);
            Assert.True(result.WasRolledBack);
            // rb1.cs should be restored to original content.
            Assert.Equal("original1", File.ReadAllText(_repo.Abs("rb1.cs")));
        }

        [Fact]
        public async Task BatchEdits_DryRun_NoFilesChanged()
        {
            _repo.CreateFile("dryb.cs", "unchanged");
            var result = await _svc.ApplyBatchEditsAsync(new BatchEditRequest
            {
                DryRun = true,
                Edits  = {
                    new BatchEditItem { Operation = "replace_text", Path = "dryb.cs",
                                        OldText = "unchanged", NewText = "changed" }
                }
            });
            Assert.True(result.WasDryRun);
            Assert.Equal("unchanged", File.ReadAllText(_repo.Abs("dryb.cs")));
        }

        // ── Rollback ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Rollback_ValidToken_RestoresOriginalContent()
        {
            _repo.CreateFile("rollme.cs", "original content");
            var writeResult = await _svc.WriteFileAsync(new WriteFileRequest
            {
                Path    = "rollme.cs",
                Content = "modified content",
                Options = new EditOptions { CreateBackup = true }
            });

            Assert.False(string.IsNullOrEmpty(writeResult.RollbackToken));
            Assert.Equal("modified content", File.ReadAllText(_repo.Abs("rollme.cs")));

            var rollback = await _svc.RollbackAsync(
                new RollbackRequest { RollbackToken = writeResult.RollbackToken });

            Assert.True(rollback.Success);
            Assert.Equal("original content", File.ReadAllText(_repo.Abs("rollme.cs")));
        }

        [Fact]
        public async Task Rollback_InvalidToken_ThrowsKeyNotFoundException()
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _svc.RollbackAsync(new RollbackRequest { RollbackToken = "INVALID_TOKEN" }));
        }

        [Fact]
        public async Task Rollback_TokenIsConsumedAfterUse()
        {
            _repo.CreateFile("once.cs", "v1");
            var r = await _svc.WriteFileAsync(new WriteFileRequest
            { Path = "once.cs", Content = "v2", Options = new EditOptions { CreateBackup = true } });

            await _svc.RollbackAsync(new RollbackRequest { RollbackToken = r.RollbackToken });
            // Token is now consumed — second use must fail.
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _svc.RollbackAsync(new RollbackRequest { RollbackToken = r.RollbackToken }));
        }

        // ── PreviewChanges ───────────────────────────────────────────────────────

        [Fact]
        public async Task PreviewChanges_ReturnsDiffAndApprovalToken()
        {
            _repo.CreateFile("preview.cs", "before");
            var response = await _svc.PreviewChangesAsync(new PreviewChangesRequest
            {
                ProposedEdits = new BatchEditRequest
                {
                    Edits = {
                        new BatchEditItem { Operation = "replace_text", Path = "preview.cs",
                                            OldText = "before", NewText = "after" }
                    }
                }
            });

            Assert.NotEmpty(response.FilePreviews);
            Assert.False(string.IsNullOrEmpty(response.ApprovalToken));
            Assert.True(response.TokenExpiry > DateTimeOffset.UtcNow);
            // File must be unchanged (dry-run).
            Assert.Equal("before", File.ReadAllText(_repo.Abs("preview.cs")));
        }

        // ── Internal diff / patch helpers ────────────────────────────────────────

        [Fact]
        public void GenerateUnifiedDiff_IdenticalContent_ReturnsEmpty()
        {
            var diff = EditService.GenerateUnifiedDiff("same", "same", "file.cs");
            Assert.Equal(string.Empty, diff);
        }

        [Fact]
        public void GenerateUnifiedDiff_ChangedLine_ContainsPlusAndMinus()
        {
            var diff = EditService.GenerateUnifiedDiff("old line\n", "new line\n", "f.cs");
            Assert.Contains("-old line", diff);
            Assert.Contains("+new line", diff);
        }

        [Fact]
        public void ApplyPatch_SimpleChange_ProducesCorrectResult()
        {
            const string original = "line1\nline2\nline3\n";
            const string patch    = @"--- a/f.cs
+++ b/f.cs
@@ -1,3 +1,3 @@
 line1
-line2
+line2_new
 line3
";
            var result = EditService.ApplyPatch(original, patch, fuzzFactor: 0);
            Assert.Contains("line2_new", result);
            Assert.DoesNotContain("line2\n", result);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static EditService Build(string root, bool readOnly)
        {
            var settings  = McpServiceSettings.CreateForTesting(
                repositoryRoot    : root,
                readOnly          : readOnly,
                blockedFolders    : new[] { ".git", "bin", "obj" },
                blockedExtensions : new[] { ".exe", ".dll" });
            var validator = new PathValidator(settings);
            var security  = new SecurityService(settings, validator);
            var audit     = new AuditService(settings);
            return new EditService(settings, validator, security, audit);
        }
    }
}
