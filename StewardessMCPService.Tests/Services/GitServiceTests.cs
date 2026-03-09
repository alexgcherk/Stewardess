using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Unit tests for <see cref="GitService"/>.
    ///
    /// Tests that rely on actual git processes are guarded by a skip condition
    /// so they degrade gracefully in CI environments where git is not installed.
    /// Tests for internal parsing helpers are always executed.
    /// </summary>
    public sealed class GitServiceTests : IDisposable
    {
        private readonly TempRepository _repo;
        private readonly GitService     _svc;

        public GitServiceTests()
        {
            _repo = new TempRepository();
            _svc  = Build(_repo.Root);
        }

        public void Dispose() => _repo.Dispose();

        // ── IsGitRepositoryAsync ─────────────────────────────────────────────────

        [Fact]
        public async Task IsGitRepository_NoDotGit_ReturnsFalse()
        {
            // Temp dir has no .git — should return false
            var result = await _svc.IsGitRepositoryAsync();
            Assert.False(result);
        }

        [Fact]
        public async Task IsGitRepository_WithDotGitDir_ReturnsTrue()
        {
            // Create a .git directory to simulate a real repo.
            Directory.CreateDirectory(Path.Combine(_repo.Root, ".git"));
            var result = await _svc.IsGitRepositoryAsync();
            Assert.True(result);
        }

        // ── GetStatusAsync ───────────────────────────────────────────────────────

        [Fact]
        public async Task GetStatus_NonGitDir_ReturnsIsGitRepositoryFalse()
        {
            var response = await _svc.GetStatusAsync(new GitStatusRequest());
            Assert.False(response.IsGitRepository);
            Assert.Empty(response.Files);
        }

        // ── Parsing: ParsePorcelainStatus ────────────────────────────────────────

        [Fact]
        public void ParsePorcelainStatus_EmptyString_ReturnsEmpty()
        {
            var entries = GitService.ParsePorcelainStatus("");
            Assert.Empty(entries);
        }

        [Fact]
        public void ParsePorcelainStatus_ModifiedStagedFile_ParsesCorrectly()
        {
            // 'M ' = staged modification
            var output = "M  src/Program.cs\n";
            var entries = GitService.ParsePorcelainStatus(output);

            Assert.Single(entries);
            var e = entries[0];
            Assert.Equal("M", e.IndexStatus);
            Assert.True(e.IsStaged);
            Assert.False(e.IsUntracked);
        }

        [Fact]
        public void ParsePorcelainStatus_UntrackedFile_ParsedAsUntracked()
        {
            var output = "?? new-file.cs\n";
            var entries = GitService.ParsePorcelainStatus(output);

            Assert.Single(entries);
            Assert.True(entries[0].IsUntracked);
            Assert.False(entries[0].IsStaged);
        }

        [Fact]
        public void ParsePorcelainStatus_RenamedFile_ExtractsOldAndNewPath()
        {
            var output = "R  old-name.cs -> new-name.cs\n";
            var entries = GitService.ParsePorcelainStatus(output);

            Assert.Single(entries);
            Assert.Equal("old-name.cs", entries[0].OldPath);
            Assert.Equal("new-name.cs", entries[0].RelativePath);
        }

        [Fact]
        public void ParsePorcelainStatus_MultipleFiles_ParsesAll()
        {
            var output = "M  src/A.cs\n?? src/B.cs\n D src/C.cs\n";
            var entries = GitService.ParsePorcelainStatus(output);

            Assert.Equal(3, entries.Count);
        }

        // ── Parsing: ParseDiffOutput ─────────────────────────────────────────────

        [Fact]
        public void ParseDiffOutput_EmptyString_ReturnsEmptyResponse()
        {
            var response = GitService.ParseDiffOutput("", "unstaged");
            Assert.Empty(response.Files);
            Assert.Equal("unstaged", response.Scope);
        }

        [Fact]
        public void ParseDiffOutput_SingleHunk_ParsesCorrectly()
        {
            const string diff = @"diff --git a/src/File.cs b/src/File.cs
index 1234567..abcdefg 100644
--- a/src/File.cs
+++ b/src/File.cs
@@ -1,3 +1,3 @@
 context line
-old line
+new line
 end
";
            var response = GitService.ParseDiffOutput(diff, "unstaged");

            Assert.Single(response.Files);
            var file = response.Files[0];
            Assert.Equal("src/File.cs", file.RelativePath);
            Assert.Equal(1, file.LinesAdded);
            Assert.Equal(1, file.LinesRemoved);
            Assert.Equal(1, response.TotalLinesAdded);
            Assert.Equal(1, response.TotalLinesRemoved);

            Assert.Single(file.Hunks);
            var hunk = file.Hunks[0];
            Assert.Equal(4, hunk.Lines.Count);  // context, removed, added, context
        }

        [Fact]
        public void ParseDiffOutput_NewFile_ChangeTypeIsAdded()
        {
            const string diff = @"diff --git a/new.cs b/new.cs
new file mode 100644
index 0000000..1234567
--- /dev/null
+++ b/new.cs
@@ -0,0 +1,3 @@
+line1
+line2
+line3
";
            var response = GitService.ParseDiffOutput(diff, "unstaged");
            Assert.Single(response.Files);
            Assert.Equal("added", response.Files[0].ChangeType);
            Assert.Equal(3, response.TotalLinesAdded);
        }

        [Fact]
        public void ParseDiffOutput_MultipleFiles_ParsesAll()
        {
            const string diff = @"diff --git a/a.cs b/a.cs
index 111..222 100644
--- a/a.cs
+++ b/a.cs
@@ -1,1 +1,1 @@
-old
+new
diff --git a/b.cs b/b.cs
index 333..444 100644
--- a/b.cs
+++ b/b.cs
@@ -1,1 +1,1 @@
-foo
+bar
";
            var response = GitService.ParseDiffOutput(diff, "staged");
            Assert.Equal(2, response.Files.Count);
            Assert.Equal(2, response.TotalLinesAdded);
            Assert.Equal(2, response.TotalLinesRemoved);
        }

        // ── Parsing: ParseShowOutput ─────────────────────────────────────────────

        [Fact]
        public void ParseShowOutput_EmptyString_ReturnsNotFound()
        {
            var result = GitService.ParseShowOutput("");
            Assert.True(result.NotFound);
        }

        [Fact]
        public void ParseShowOutput_ValidOutput_ParsesAllFields()
        {
            // Simulate: git show --no-patch --format=%H%x1F%h%x1F%an%x1F%ae%x1F%aI%x1F%cn%x1F%ce%x1F%cI%x1F%s%x1F%b%x1F%P --name-only
            var sep  = "\x1F";
            var line = string.Join(sep, new[]
            {
                "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef",  // %H
                "deadbee",                                    // %h
                "Bob",                                        // %an
                "bob@example.com",                            // %ae
                "2024-06-01T09:30:00+00:00",                  // %aI
                "Bob",                                        // %cn
                "bob@corp.example",                           // %ce
                "2024-06-01T09:30:00+00:00",                  // %cI
                "Add feature X",                              // %s
                "Longer description here",         // %b  (no embedded newlines: Split('\n') would fragment the format line)
                "abc1234"                                     // %P
            });

            var output = line + "\n\nsrc/Feature.cs\nsrc/Tests/FeatureTests.cs\n";
            var result = GitService.ParseShowOutput(output);

            Assert.False(result.NotFound);
            Assert.Equal("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", result.Sha);
            Assert.Equal("deadbee",          result.ShortSha);
            Assert.Equal("Bob",              result.AuthorName);
            Assert.Equal("bob@example.com",  result.AuthorEmail);
            Assert.Equal("bob@corp.example", result.CommitterEmail);
            Assert.Equal("Add feature X",    result.Subject);
            Assert.Single(result.ParentShas);
            Assert.Equal("abc1234",          result.ParentShas[0]);
            Assert.Equal(2,                  result.ChangedFiles.Count);
            Assert.Contains("src/Feature.cs", result.ChangedFiles);
        }

        [Fact]
        public void ParseShowOutput_MergeCommit_ParsesMultipleParents()
        {
            var sep  = "\x1F";
            var line = string.Join(sep, new[]
            {
                "aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111", // %H
                "aaaa111",   // %h
                "Carol",     // %an
                "carol@x",   // %ae
                "2024-07-01T00:00:00+00:00", // %aI
                "Carol",     // %cn
                "carol@x",   // %ce
                "2024-07-01T00:00:00+00:00", // %cI
                "Merge feature",  // %s
                "",               // %b
                "parent1sha parent2sha"  // %P — two parents
            });

            var result = GitService.ParseShowOutput(line + "\n");
            Assert.False(result.NotFound);
            Assert.Equal(2, result.ParentShas.Count);
            Assert.Equal("parent1sha", result.ParentShas[0]);
            Assert.Equal("parent2sha", result.ParentShas[1]);
        }

        // ── GetCommitAsync: argument validation ──────────────────────────────────

        [Fact]
        public async Task GetCommitAsync_NullRequest_ThrowsArgumentException()
        {
            // Null SHA inside the default request should throw.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _svc.GetCommitAsync(new GitShowRequest { Sha = "" }, CancellationToken.None));
        }

        [Theory]
        [InlineData("bad\"sha")]          // double-quote
        [InlineData("bad;sha")]           // semicolon
        [InlineData("bad|sha")]           // pipe
        [InlineData("bad&sha")]           // ampersand
        [InlineData("bad`sha")]           // backtick
        [InlineData("bad sha")]           // space (length ok, but invalid char)
        public async Task GetCommitAsync_InvalidSha_ThrowsArgumentException(string sha)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _svc.GetCommitAsync(new GitShowRequest { Sha = sha }, CancellationToken.None));
        }

        [Fact]
        public async Task GetCommitAsync_NonGitRepo_ReturnsNotFound()
        {
            // Temp dir has no .git → service should return NotFound gracefully.
            var result = await _svc.GetCommitAsync(
                new GitShowRequest { Sha = "abc1234" }, CancellationToken.None);
            Assert.True(result.NotFound);
        }

        // ── Parsing: ParseLogOutput ──────────────────────────────────────────────

        [Fact]
        public void ParseLogOutput_EmptyString_ReturnsEmpty()
        {
            var response = GitService.ParseLogOutput("", 20);
            Assert.Empty(response.Commits);
        }

        [Fact]
        public void ParseLogOutput_SingleCommit_ParsesFields()
        {
            // Simulate git log --format=%H%x1F%h%x1F%an%x1F%ae%x1F%aI%x1F%cn%x1F%cI%x1F%s%x1F%b%x1F%P
            var sep = "\x1F";
            var line = string.Join(sep, new[]
            {
                "abc1234abc1234abc1234abc1234abc1234abc1234",   // %H
                "abc1234",                                       // %h
                "Alice",                                         // %an
                "alice@example.com",                             // %ae
                "2024-01-15T10:00:00+00:00",                    // %aI
                "Alice",                                         // %cn
                "2024-01-15T10:00:00+00:00",                    // %cI
                "Fix bug in parser",                             // %s
                "",                                              // %b
                ""                                               // %P (no parents)
            });

            var response = GitService.ParseLogOutput(line + "\n", 20);
            Assert.Single(response.Commits);

            var commit = response.Commits[0];
            Assert.Equal("abc1234abc1234abc1234abc1234abc1234abc1234", commit.Sha);
            Assert.Equal("abc1234",         commit.ShortSha);
            Assert.Equal("Alice",           commit.AuthorName);
            Assert.Equal("alice@example.com", commit.AuthorEmail);
            Assert.Equal("Fix bug in parser", commit.Subject);
        }

        // ── Security regression: BuildPathSuffix injection (S02) ────────────────

        [Fact]
        public void BuildPathSuffix_NormalPath_ReturnsQuotedSuffix()
        {
            var suffix = GitService.BuildPathSuffix("src/Program.cs");
            Assert.Equal(" -- \"src/Program.cs\"", suffix);
        }

        [Fact]
        public void BuildPathSuffix_BackslashPath_NormalisedToForwardSlash()
        {
            var suffix = GitService.BuildPathSuffix(@"src\Program.cs");
            Assert.Equal(" -- \"src/Program.cs\"", suffix);
        }

        [Theory]
        [InlineData("foo\" --inject-flag \"rest")]   // double-quote injection
        [InlineData("foo\0bar")]                      // null byte
        public void BuildPathSuffix_PathWithDangerousChars_ReturnsEmpty(string maliciousPath)
        {
            // A path containing " would break the argument quoting and let an attacker
            // inject arbitrary git options.  Must return "" (no path filter) instead.
            var suffix = GitService.BuildPathSuffix(maliciousPath);
            Assert.Equal("", suffix);
        }

        [Fact]
        public void BuildPathSuffix_Null_ReturnsEmpty()
        {
            Assert.Equal("", GitService.BuildPathSuffix(null!));
        }

        // ── Security regression: BuildDiffScopeArgs injection (S03) ─────────────

        [Theory]
        [InlineData("staged",   "--cached")]
        [InlineData("STAGED",   "--cached")]
        [InlineData("head",     "HEAD")]
        [InlineData("unstaged", "")]
        [InlineData("unknown",  "")]
        [InlineData(null,       "")]
        public void BuildDiffScopeArgs_KnownScopes_ReturnFixedArgs(string? scope, string expected)
        {
            Assert.Equal(expected, GitService.BuildDiffScopeArgs(scope));
        }

        [Fact]
        public void BuildDiffScopeArgs_CommitScope_WrapsRangeInQuotes()
        {
            // "commit:sha1..sha2" should wrap the range in double-quotes so it cannot
            // inject additional flags via unquoted whitespace.
            var result = GitService.BuildDiffScopeArgs("commit:abc123..def456");
            Assert.Equal("\"abc123..def456\"", result);
        }

        [Fact]
        public void BuildDiffScopeArgs_CommitScopeWithInjection_ValueIsWrapped()
        {
            // Even if an attacker appends " --output=C:\evil", the whole string
            // becomes one quoted argument and cannot split into separate git options.
            var malicious = "commit:HEAD --output=C:\\evil.txt";
            var result    = GitService.BuildDiffScopeArgs(malicious);
            // Result must start and end with " so it is a single quoted token.
            Assert.StartsWith("\"", result);
            Assert.EndsWith("\"", result);
            // The injected flag must be inside the quotes, not outside.
            Assert.DoesNotContain("\" --output", result);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static GitService Build(string root) =>
            new GitService(
                McpServiceSettings.CreateForTesting(root),
                new PathValidator(McpServiceSettings.CreateForTesting(root)));
    }
}
