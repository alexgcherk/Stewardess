// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using StewardessMCPService.CodeIndexing.Parsers.Python;
using Xunit;

namespace StewardessMCPService.CodeIndexing.Tests.Parsers;

public class PythonParserAdapterTests
{
    private readonly PythonParserAdapter _adapter = new();

    private static string GoldenFile(string name) =>
        Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Python", name);

    [Fact]
    public void LanguageId_IsPython()
    {
        Assert.Equal("python", _adapter.LanguageId);
    }

    [Fact]
    public void Capabilities_HeuristicMode()
    {
        Assert.True(_adapter.Capabilities.SupportsHeuristicFallback);
        Assert.True(_adapter.Capabilities.SupportsOutline);
    }

    [Fact]
    public async Task ParseAsync_SampleModule_ReturnsSuccess()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.NotEmpty(result.Nodes);
        Assert.Equal(ExtractionMode.Heuristic, result.ExtractionMode);
    }

    [Fact]
    public async Task ParseAsync_SampleModule_ExtractsAnimalClass()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var cls = result.Nodes.FirstOrDefault(n =>
            n.Kind == NodeKind.Declaration && n.Name == "Animal");
        Assert.NotNull(cls);
        Assert.Equal("class", cls.Subkind);
    }

    [Fact]
    public async Task ParseAsync_SampleModule_ExtractsDogSubclass()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        Assert.Contains(result.Nodes, n =>
            n.Kind == NodeKind.Declaration && n.Name == "Dog");
    }

    [Fact]
    public async Task ParseAsync_SampleModule_ExtractsTopLevelFunctions()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var funcs = result.Nodes.Where(n =>
            n.Kind == NodeKind.Callable && n.Subkind == "function").ToList();
        Assert.True(funcs.Count >= 2, $"Expected >= 2 top-level functions, got {funcs.Count}");
    }

    [Fact]
    public async Task ParseAsync_SampleModule_ExtractsMethods()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var methods = result.Nodes.Where(n =>
            n.Kind == NodeKind.Callable && n.Subkind == "method").ToList();
        Assert.True(methods.Count >= 3, $"Expected >= 3 methods, got {methods.Count}");
    }

    [Fact]
    public async Task ParseAsync_SampleModule_AsyncFunctionHasAsyncModifier()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var asyncFunc = result.Nodes.FirstOrDefault(n =>
            n.Name == "fetch_data");
        Assert.NotNull(asyncFunc);
        Assert.Contains("async", asyncFunc!.Modifiers);
    }

    [Fact]
    public async Task ParseAsync_SampleModule_NodesHaveSourceSpans()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        Assert.All(result.Nodes, n =>
        {
            Assert.NotNull(n.SourceSpan);
            Assert.True(n.SourceSpan!.StartLine > 0);
        });
    }

    [Fact]
    public async Task ParseAsync_SampleModule_ChildrenPointToExistingNodes()
    {
        var content = await File.ReadAllTextAsync(GoldenFile("SampleModule.py"));
        var req = MakeRequest(content);

        var result = await _adapter.ParseAsync(req);

        var nodeIds = result.Nodes.Select(n => n.NodeId).ToHashSet();
        foreach (var node in result.Nodes)
            foreach (var childId in node.Children)
                Assert.Contains(childId, nodeIds);
    }

    [Fact]
    public async Task ParseAsync_EmptyFile_ReturnsSuccessWithNoNodes()
    {
        var req = new ParseRequest
        {
            FileId = "empty-py-1",
            FilePath = "empty.py",
            Content = "",
            LanguageId = "python",
            Mode = ParseMode.Declarations,
        };

        var result = await _adapter.ParseAsync(req);

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public async Task ParseAsync_NestedClass_IsChildOfOuterClass()
    {
        const string code = @"
class Outer:
    class Inner:
        def inner_method(self):
            pass
    def outer_method(self):
        pass
";
        var req = new ParseRequest
        {
            FileId = "nested-1",
            FilePath = "nested.py",
            Content = code,
            LanguageId = "python",
            Mode = ParseMode.Declarations,
        };

        var result = await _adapter.ParseAsync(req);

        var outer = result.Nodes.FirstOrDefault(n => n.Name == "Outer");
        var inner = result.Nodes.FirstOrDefault(n => n.Name == "Inner");
        Assert.NotNull(outer);
        Assert.NotNull(inner);
        Assert.Equal(outer!.NodeId, inner!.ParentNodeId);
    }

    private static ParseRequest MakeRequest(string content) => new()
    {
        FileId = "test-py-1",
        FilePath = "GoldenFiles/Python/SampleModule.py",
        Content = content,
        LanguageId = "python",
        Mode = ParseMode.Declarations,
    };
}
