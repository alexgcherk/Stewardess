// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using StewardessMCPService.CodeIndexing.Projection;
using StewardessMCPService.Parsers.CSharp;
using Xunit;

namespace StewardessMCPService.CodeIndexing.Tests.Projection;

/// <summary>
/// Integration tests for <see cref="CSharpSymbolProjector"/> using the
/// canonical SampleClass.cs golden file. Verifies that all expected symbols
/// and occurrences are produced with correct kinds, parent chains, and IDs.
/// </summary>
public class CSharpSymbolProjectorTests
{
    private const string FileId = "test-sampleclass";
    private const string RepoScope = "stewardess";

    private readonly CSharpParserAdapter _adapter = new();
    private readonly CSharpSymbolProjector _projector = new();

    private static string GoldenFile(string name) =>
        Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "CSharp", name);

    /// <summary>Parses SampleClass.cs and projects it to symbols.</summary>
    private async Task<SymbolProjectionResult> ProjectSampleClassAsync()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var parseReq = new ParseRequest
        {
            FileId = FileId,
            FilePath = "GoldenFiles/CSharp/SampleClass.cs",
            Content = content,
            LanguageId = "csharp",
            Mode = ParseMode.Declarations,
        };
        var parseResult = await _adapter.ParseAsync(parseReq);
        var nodeMap = parseResult.Nodes.ToDictionary(n => n.NodeId);
        return _projector.Project(FileId, RepoScope, nodeMap);
    }

    // ── Basic projection ─────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_ReturnsSymbols()
    {
        var result = await ProjectSampleClassAsync();
        Assert.NotEmpty(result.Symbols);
    }

    [Fact]
    public async Task Project_SampleClass_ReturnsOccurrences()
    {
        var result = await ProjectSampleClassAsync();
        Assert.NotEmpty(result.Occurrences);
        // One occurrence per symbol (all Declaration role)
        Assert.Equal(result.Symbols.Count, result.Occurrences.Count);
    }

    // ── Namespace symbol ─────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_ExtractsNamespaceSymbol()
    {
        var result = await ProjectSampleClassAsync();
        var ns = result.Symbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Namespace && s.Name == "SampleNamespace");
        Assert.NotNull(ns);
    }

    [Fact]
    public async Task Project_SampleClass_NamespaceHasNsCategory()
    {
        var result = await ProjectSampleClassAsync();
        var ns = result.Symbols.First(s => s.Kind == SymbolKind.Namespace);
        Assert.Contains(":ns:", ns.SymbolId);
    }

    // ── Class symbols ────────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_ExtractsPersonClass()
    {
        var result = await ProjectSampleClassAsync();
        var cls = result.Symbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Class && s.Name == "Person");
        Assert.NotNull(cls);
        Assert.Equal("SampleNamespace.Person", cls.QualifiedName);
    }

    [Fact]
    public async Task Project_SampleClass_PersonClassHasNamespaceAsParent()
    {
        var result = await ProjectSampleClassAsync();
        var ns = result.Symbols.First(s => s.Kind == SymbolKind.Namespace);
        var cls = result.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Person");
        Assert.Equal(ns.SymbolId, cls.ParentSymbolId);
    }

    [Fact]
    public async Task Project_SampleClass_ExtractsPersonRepositoryClass()
    {
        var result = await ProjectSampleClassAsync();
        Assert.Contains(result.Symbols, s =>
            s.Kind == SymbolKind.Class && s.Name == "PersonRepository");
    }

    // ── Interface symbol ─────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_ExtractsIRepositoryInterface()
    {
        var result = await ProjectSampleClassAsync();
        var iface = result.Symbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Interface && s.Name == "IRepository");
        Assert.NotNull(iface);
        Assert.Contains(":type:", iface.SymbolId);
    }

    // ── Enum symbol ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_ExtractsStatusEnum()
    {
        var result = await ProjectSampleClassAsync();
        var e = result.Symbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Enum && s.Name == "Status");
        Assert.NotNull(e);
    }

    // ── Method / constructor symbols ─────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_ExtractsPersonConstructor()
    {
        var result = await ProjectSampleClassAsync();
        Assert.Contains(result.Symbols, s =>
            s.Kind == SymbolKind.Constructor && s.Name == "Person");
    }

    [Fact]
    public async Task Project_SampleClass_ExtractsGreetMethod()
    {
        var result = await ProjectSampleClassAsync();
        Assert.Contains(result.Symbols, s =>
            s.Kind == SymbolKind.Method && s.Name == "Greet");
    }

    [Fact]
    public async Task Project_SampleClass_MethodsHaveCallableCategory()
    {
        var result = await ProjectSampleClassAsync();
        var methods = result.Symbols.Where(s =>
            s.Kind is SymbolKind.Method or SymbolKind.Constructor).ToList();
        Assert.All(methods, m => Assert.Contains(":callable:", m.SymbolId));
    }

    // ── Property / field symbols ─────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_ExtractsNameAndAgeProperties()
    {
        var result = await ProjectSampleClassAsync();
        var props = result.Symbols.Where(s =>
            s.Kind == SymbolKind.Property).ToList();
        Assert.Contains(props, p => p.Name == "Name");
        Assert.Contains(props, p => p.Name == "Age");
    }

    // ── Symbol ID stability ──────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_SymbolIdsAreStable_AcrossTwoRuns()
    {
        var r1 = await ProjectSampleClassAsync();
        var r2 = await ProjectSampleClassAsync();
        var ids1 = r1.Symbols.Select(s => s.SymbolId).OrderBy(x => x).ToList();
        var ids2 = r2.Symbols.Select(s => s.SymbolId).OrderBy(x => x).ToList();
        Assert.Equal(ids1, ids2);
    }

    [Fact]
    public async Task Project_SampleClass_AllSymbolIdsContainLanguageId()
    {
        var result = await ProjectSampleClassAsync();
        Assert.All(result.Symbols, s => Assert.StartsWith("csharp:", s.SymbolId));
    }

    [Fact]
    public async Task Project_SampleClass_AllSymbolIdsContainRepoScope()
    {
        var result = await ProjectSampleClassAsync();
        Assert.All(result.Symbols, s => Assert.Contains($":{RepoScope}:", s.SymbolId));
    }

    // ── Occurrences ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Project_SampleClass_AllOccurrencesArePrimaryDeclarations()
    {
        var result = await ProjectSampleClassAsync();
        Assert.All(result.Occurrences, occ =>
        {
            Assert.Equal(OccurrenceRole.Declaration, occ.Role);
            Assert.True(occ.IsPrimary);
        });
    }

    [Fact]
    public async Task Project_SampleClass_OccurrencesReferenceCorrectFileId()
    {
        var result = await ProjectSampleClassAsync();
        Assert.All(result.Occurrences, occ => Assert.Equal(FileId, occ.FileId));
    }

    [Fact]
    public async Task Project_SampleClass_OccurrenceIdsContainFileId()
    {
        var result = await ProjectSampleClassAsync();
        Assert.All(result.Occurrences, occ => Assert.Contains(FileId, occ.OccurrenceId));
    }

    [Fact]
    public async Task Project_SampleClass_EachSymbolOccurrenceMatchesSymbol()
    {
        var result = await ProjectSampleClassAsync();
        var symbolIds = result.Symbols.Select(s => s.SymbolId).ToHashSet();
        Assert.All(result.Occurrences, occ => Assert.Contains(occ.SymbolId, symbolIds));
    }
}
