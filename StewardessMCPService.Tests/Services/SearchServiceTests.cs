// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
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
    /// Unit tests for <see cref="SearchService"/>.
    /// Covers text search, regex search, file name search, and extension search.
    /// </summary>
    public sealed class SearchServiceTests : IDisposable
    {
        private readonly TempRepository _repo;
        private readonly SearchService  _svc;

        public SearchServiceTests()
        {
            _repo = new TempRepository();
            _repo.CreateSampleCsStructure();
            _svc  = Build(_repo.Root);
        }

        public void Dispose() => _repo.Dispose();

        // ── SearchText ───────────────────────────────────────────────────────────

        [Fact]
        public async Task SearchText_ExistingWord_FindsMatches()
        {
            var result = await _svc.SearchTextAsync(
                new SearchTextRequest { Query = "class Class1", IgnoreCase = false },
                CancellationToken.None);
            Assert.NotEmpty(result.Files);
        }

        [Fact]
        public async Task SearchText_MissingWord_ReturnsEmpty()
        {
            var result = await _svc.SearchTextAsync(
                new SearchTextRequest { Query = "XYZZY_NONEXISTENT_12345", IgnoreCase = false },
                CancellationToken.None);
            Assert.Empty(result.Files);
        }

        [Fact]
        public async Task SearchText_IgnoreCase_FindsCaseVariants()
        {
            var result = await _svc.SearchTextAsync(
                new SearchTextRequest { Query = "CLASS1", IgnoreCase = true },
                CancellationToken.None);
            Assert.NotEmpty(result.Files);
        }

        [Fact]
        public async Task SearchText_MaxResults_LimitsResults()
        {
            // Create 5 files each containing the word "target".
            for (int i = 0; i < 5; i++)
                _repo.CreateFile($"file{i}.cs", $"// target content {i}");

            var result = await _svc.SearchTextAsync(
                new SearchTextRequest { Query = "target", MaxResults = 2 },
                CancellationToken.None);
            Assert.True(result.TotalMatchCount <= 2 || result.Truncated);
        }

        [Fact]
        public async Task SearchText_ContextLines_IncludesContext()
        {
            _repo.CreateFile("ctx.cs", "line1\nline2\ntarget\nline4\nline5");
            var result = await _svc.SearchTextAsync(
                new SearchTextRequest
                {
                    Query              = "target",
                    IgnoreCase         = false,
                    ContextLinesBefore = 1,
                    ContextLinesAfter  = 1
                },
                CancellationToken.None);
            Assert.NotEmpty(result.Files);
        }

        // ── SearchRegex ──────────────────────────────────────────────────────────

        [Fact]
        public async Task SearchRegex_ValidPattern_FindsMatches()
        {
            var result = await _svc.SearchRegexAsync(
                new SearchRegexRequest { Pattern = @"class\s+\w+", IgnoreCase = false },
                CancellationToken.None);
            Assert.NotEmpty(result.Files);
        }

        [Fact]
        public async Task SearchRegex_InvalidPattern_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _svc.SearchRegexAsync(
                    new SearchRegexRequest { Pattern = "[invalid regex" },
                    CancellationToken.None));
        }

        [Fact]
        public async Task SearchRegex_NonMatchingPattern_ReturnsEmpty()
        {
            var result = await _svc.SearchRegexAsync(
                new SearchRegexRequest { Pattern = @"^ZYXNONEXISTENT99\d{50}$" },
                CancellationToken.None);
            Assert.Empty(result.Files);
        }

        // ── SearchFileNames ──────────────────────────────────────────────────────

        [Fact]
        public async Task SearchFileNames_WildcardPattern_FindsMatches()
        {
            var result = await _svc.SearchFileNamesAsync(
                new SearchFileNamesRequest { Pattern = "*.cs" },
                CancellationToken.None);
            Assert.NotEmpty(result.Matches);
            Assert.All(result.Matches, f =>
                Assert.EndsWith(".cs", f.RelativePath, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SearchFileNames_NoMatchPattern_ReturnsEmpty()
        {
            var result = await _svc.SearchFileNamesAsync(
                new SearchFileNamesRequest { Pattern = "*.xyz_nonexistent" },
                CancellationToken.None);
            Assert.Empty(result.Matches);
        }

        [Fact]
        public async Task SearchFileNames_PartialName_FindsFile()
        {
            var result = await _svc.SearchFileNamesAsync(
                new SearchFileNamesRequest { Pattern = "Class1*" },
                CancellationToken.None);
            Assert.NotEmpty(result.Matches);
        }

        // ── SearchByExtension ────────────────────────────────────────────────────

        [Fact]
        public async Task SearchByExtension_DotCs_FindsCsFiles()
        {
            var result = await _svc.SearchByExtensionAsync(
                new SearchByExtensionRequest { Extensions = { ".cs" } },
                CancellationToken.None);
            Assert.NotEmpty(result.Matches);
            Assert.All(result.Matches, f =>
                Assert.EndsWith(".cs", f.RelativePath, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SearchByExtension_MultipleExtensions_FindsBoth()
        {
            _repo.CreateFile("readme.md", "# Readme");
            var result = await _svc.SearchByExtensionAsync(
                new SearchByExtensionRequest { Extensions = { ".cs", ".md" } },
                CancellationToken.None);
            var hasCs = false; var hasMd = false;
            foreach (var f in result.Matches)
            {
                if (f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) hasCs = true;
                if (f.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) hasMd = true;
            }
            Assert.True(hasCs, "Expected .cs files");
            Assert.True(hasMd, "Expected .md files");
        }

        // ── SearchSymbol ─────────────────────────────────────────────────────────

        [Fact]
        public async Task SearchSymbol_ClassName_FindsDefinition()
        {
            var result = await _svc.SearchSymbolAsync(
                new SearchSymbolRequest { SymbolName = "Class1", IgnoreCase = false },
                CancellationToken.None);
            Assert.NotEmpty(result.Files);
        }

        [Fact]
        public async Task SearchSymbol_InterfaceName_FindsDefinition()
        {
            var result = await _svc.SearchSymbolAsync(
                new SearchSymbolRequest { SymbolName = "IService", SymbolKind = "interface" },
                CancellationToken.None);
            Assert.NotEmpty(result.Files);
        }

        // ── FindReferences ───────────────────────────────────────────────────────

        [Fact]
        public async Task FindReferences_ClassName_FindsUsages()
        {
            var result = await _svc.FindReferencesAsync(
                new FindReferencesRequest { IdentifierName = "Class1" },
                CancellationToken.None);
            Assert.NotEmpty(result.Files);
        }

        // ── Security regression: ReDoS protection (S07) ─────────────────────────

        [Fact]
        public async Task SearchRegex_CatastrophicBacktrackingPattern_CompletesWithinTimeout()
        {
            // (a+)+$ is a classic ReDoS pattern.  Against "aaaa...b" where there
            // is no match, a naive engine would take exponential time.
            // With a 5-second per-match timeout the search must finish quickly and
            // return a graceful result (either empty matches or a timeout indication).
            _repo.CreateFile("evil.cs",
                new string('a', 25) + "b");   // 25 a's then b — no match for (a+)+$

            var cts   = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var sw    = System.Diagnostics.Stopwatch.StartNew();

            var result = await _svc.SearchRegexAsync(
                new SearchRegexRequest { Pattern = "(a+)+$", IgnoreCase = false },
                cts.Token);

            sw.Stop();

            // Must complete well before the global 20s guard.
            Assert.True(sw.ElapsedMilliseconds < 15_000,
                $"SearchRegex took too long: {sw.ElapsedMilliseconds} ms");
            Assert.False(cts.IsCancellationRequested, "Test guard cancellation fired");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static SearchService Build(string root)
        {
            var settings  = McpServiceSettings.CreateForTesting(repositoryRoot: root);
            var validator = new PathValidator(settings);
            return new SearchService(settings, validator);
        }
    }
}
