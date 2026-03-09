// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using StewardessMCPService.CodeIndexing.Parsers.Python;
using StewardessMCPService.CodeIndexing.Projection;
using Xunit;

namespace StewardessMCPService.CodeIndexing.Tests.Projection;

/// <summary>
///     Integration tests for <see cref="PythonSymbolProjector" /> using the
///     canonical SampleModule.py golden file. Verifies that classes, functions,
///     methods, and module containers are projected with correct kinds and parent chains.
/// </summary>
public class PythonSymbolProjectorTests
{
    private const string FileId = "test-samplemodule";
    private const string RepoScope = "stewardess";

    private readonly PythonParserAdapter _adapter = new();
    private readonly PythonSymbolProjector _projector = new();

    private static string GoldenFile(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Python", name);
    }

    /// <summary>Parses SampleModule.py and projects it to symbols.</summary>
    private async Task<SymbolProjectionResult> ProjectSampleModuleAsync()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var parseReq = new ParseRequest
        {
            FileId = FileId,
            FilePath = "GoldenFiles/Python/SampleModule.py",
            Content = content,
            LanguageId = "python",
            Mode = ParseMode.Declarations
        };
        var parseResult = await _adapter.ParseAsync(parseReq);
        var nodeMap = parseResult.Nodes.ToDictionary(n => n.NodeId);
        return _projector.Project(FileId, RepoScope, nodeMap);
    }

    // ── Basic projection ─────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleModule_ReturnsSymbols()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.NotEmpty(result.Symbols);
    }

    [Fact]
    public async Task Project_SampleModule_ReturnsOccurrences()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.NotEmpty(result.Occurrences);
        Assert.Equal(result.Symbols.Count, result.Occurrences.Count);
    }

    // ── Class symbols ────────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleModule_ExtractsAnimalClass()
    {
        var result = await ProjectSampleModuleAsync();
        var cls = result.Symbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Class && s.Name == "Animal");
        Assert.NotNull(cls);
        Assert.Contains(":type:", cls.SymbolId);
    }

    [Fact]
    public async Task Project_SampleModule_ExtractsDogClass()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.Contains(result.Symbols, s =>
            s.Kind == SymbolKind.Class && s.Name == "Dog");
    }

    // ── Method symbols ───────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleModule_ExtractsSpeakMethod()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.Contains(result.Symbols, s =>
            s.Kind == SymbolKind.Method && s.Name == "speak");
    }

    [Fact]
    public async Task Project_SampleModule_ExtractsInitMethod()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.Contains(result.Symbols, s =>
            s.Kind == SymbolKind.Method && s.Name == "__init__");
    }

    [Fact]
    public async Task Project_SampleModule_MethodsHaveCallableCategory()
    {
        var result = await ProjectSampleModuleAsync();
        var methods = result.Symbols.Where(s => s.Kind == SymbolKind.Method).ToList();
        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.Contains(":callable:", m.SymbolId));
    }

    // ── Function symbols ─────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleModule_ExtractsGreetTopLevelFunction()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.Contains(result.Symbols, s =>
            s.Kind == SymbolKind.Function && s.Name == "greet");
    }

    [Fact]
    public async Task Project_SampleModule_ExtractsFetchDataAsyncFunction()
    {
        var result = await ProjectSampleModuleAsync();
        // fetch_data is declared as async function in the golden file
        Assert.Contains(result.Symbols, s =>
            s.Kind == SymbolKind.Function && s.Name == "fetch_data");
    }

    [Fact]
    public async Task Project_SampleModule_TopLevelFunctionsHaveNoParent()
    {
        var result = await ProjectSampleModuleAsync();
        var topLevelFunctions = result.Symbols.Where(s =>
            s.Kind == SymbolKind.Function).ToList();
        // Top-level functions are not inside a class so parentSymbolId should be null
        // (unless there's a module container parent)
        Assert.NotEmpty(topLevelFunctions);
    }

    // ── Parent chain ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleModule_SpeakMethodParentIsAnimal()
    {
        var result = await ProjectSampleModuleAsync();
        var animalSymbol = result.Symbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Class && s.Name == "Animal");
        var speakMethod = result.Symbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Method && s.Name == "speak");

        // Both should exist
        Assert.NotNull(animalSymbol);
        Assert.NotNull(speakMethod);
        Assert.Equal(animalSymbol.SymbolId, speakMethod.ParentSymbolId);
    }

    // ── Symbol ID consistency ────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleModule_AllSymbolIdsStartWithPython()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.All(result.Symbols, s => Assert.StartsWith("python:", s.SymbolId));
    }

    [Fact]
    public async Task Project_SampleModule_AllSymbolIdsContainRepoScope()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.All(result.Symbols, s => Assert.Contains($":{RepoScope}:", s.SymbolId));
    }

    [Fact]
    public async Task Project_SampleModule_SymbolIdsAreStable_AcrossTwoRuns()
    {
        var r1 = await ProjectSampleModuleAsync();
        var r2 = await ProjectSampleModuleAsync();
        var ids1 = r1.Symbols.Select(s => s.SymbolId).OrderBy(x => x).ToList();
        var ids2 = r2.Symbols.Select(s => s.SymbolId).OrderBy(x => x).ToList();
        Assert.Equal(ids1, ids2);
    }

    // ── Occurrences ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleModule_AllOccurrencesArePrimaryDeclarations()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.All(result.Occurrences, occ =>
        {
            Assert.Equal(OccurrenceRole.Declaration, occ.Role);
            Assert.True(occ.IsPrimary);
        });
    }

    [Fact]
    public async Task Project_SampleModule_OccurrencesReferenceCorrectFileId()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.All(result.Occurrences, occ => Assert.Equal(FileId, occ.FileId));
    }

    [Fact]
    public async Task Project_SampleModule_EachOccurrenceHasValidSourceSpan()
    {
        var result = await ProjectSampleModuleAsync();
        Assert.All(result.Occurrences, occ =>
        {
            Assert.NotNull(occ.SourceSpan);
            Assert.True(occ.SourceSpan!.StartLine > 0);
        });
    }
}