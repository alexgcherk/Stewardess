using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using StewardessMCPService.Parsers.CSharp;
using Xunit;

namespace StewardessMCPService.CodeIndexing.Tests.Parsers;

public class CSharpParserAdapterTests
{
    private readonly CSharpParserAdapter _adapter = new();

    private static string GoldenFile(string name) =>
        Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "CSharp", name);

    [Fact]
    public void LanguageId_IsCSharp()
    {
        Assert.Equal("csharp", _adapter.LanguageId);
    }

    [Fact]
    public void Capabilities_OutlineAndDeclarationsSupported()
    {
        Assert.True(_adapter.Capabilities.SupportsOutline);
        Assert.True(_adapter.Capabilities.SupportsDeclarations);
    }

    [Fact]
    public async Task ParseAsync_SampleClass_ReturnsSuccess()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = new ParseRequest
        {
            FileId = "test-1",
            FilePath = "GoldenFiles/CSharp/SampleClass.cs",
            Content = content,
            LanguageId = "csharp",
            Mode = ParseMode.Declarations,
        };

        var result = await _adapter.ParseAsync(req);

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.NotEmpty(result.Nodes);
        Assert.Equal(ExtractionMode.CompilerSyntax, result.ExtractionMode);
    }

    [Fact]
    public async Task ParseAsync_SampleClass_ExtractsNamespace()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var ns = result.Nodes.FirstOrDefault(n =>
            n.Kind == NodeKind.Container && n.Subkind == "namespace");
        Assert.NotNull(ns);
        Assert.Equal("SampleNamespace", ns.Name);
    }

    [Fact]
    public async Task ParseAsync_SampleClass_ExtractsPersonClass()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var cls = result.Nodes.FirstOrDefault(n =>
            n.Kind == NodeKind.Declaration && n.Name == "Person");
        Assert.NotNull(cls);
        Assert.Equal("class", cls.Subkind);
        Assert.Contains("public", cls.Modifiers);
    }

    [Fact]
    public async Task ParseAsync_SampleClass_ExtractsInterface()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var iface = result.Nodes.FirstOrDefault(n =>
            n.Kind == NodeKind.Declaration && n.Name == "IRepository");
        Assert.NotNull(iface);
        Assert.Equal("interface", iface.Subkind);
    }

    [Fact]
    public async Task ParseAsync_SampleClass_ExtractsMethods()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var methods = result.Nodes.Where(n => n.Kind == NodeKind.Callable).ToList();
        Assert.True(methods.Count >= 4, $"Expected >= 4 methods, got {methods.Count}");
    }

    [Fact]
    public async Task ParseAsync_SampleClass_ExtractsEnum()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var @enum = result.Nodes.FirstOrDefault(n =>
            n.Kind == NodeKind.Declaration && n.Subkind == "enum");
        Assert.NotNull(@enum);
        Assert.Equal("Status", @enum.Name);
    }

    [Fact]
    public async Task ParseAsync_SampleClass_NodesHaveSourceSpans()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        Assert.All(result.Nodes, n =>
        {
            Assert.NotNull(n.SourceSpan);
            Assert.True(n.SourceSpan!.StartLine > 0);
        });
    }

    [Fact]
    public async Task ParseAsync_SampleClass_NodesHaveConfidenceOne()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        Assert.All(result.Nodes, n => Assert.Equal(1.0, n.Confidence));
    }

    [Fact]
    public async Task ParseAsync_InvalidCSharp_ReturnsPartialOrSuccess()
    {
        var req = new ParseRequest
        {
            FileId = "bad-1",
            FilePath = "bad.cs",
            Content = "public class Broken { void Method( { } }",
            LanguageId = "csharp",
            Mode = ParseMode.Declarations,
        };

        var result = await _adapter.ParseAsync(req);

        // Roslyn should still parse it with partial recovery
        Assert.True(result.Status == ParseStatus.Success || result.Status == ParseStatus.Partial,
            $"Expected Success or Partial, got {result.Status}");
    }

    [Fact]
    public async Task ParseAsync_EmptyFile_ReturnsSuccessWithNoNodes()
    {
        var req = new ParseRequest
        {
            FileId = "empty-1",
            FilePath = "empty.cs",
            Content = "",
            LanguageId = "csharp",
            Mode = ParseMode.Declarations,
        };

        var result = await _adapter.ParseAsync(req);

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public async Task ParseAsync_ChildrenPointToExistingNodes()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleClass.cs"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var nodeIds = result.Nodes.Select(n => n.NodeId).ToHashSet();
        foreach (var node in result.Nodes)
            foreach (var childId in node.Children)
                Assert.Contains(childId, nodeIds);
    }

    private static ParseRequest MakeRequest(string content) => new()
    {
        FileId = "test-1",
        FilePath = "GoldenFiles/CSharp/SampleClass.cs",
        Content = content,
        LanguageId = "csharp",
        Mode = ParseMode.Declarations,
    };
}
