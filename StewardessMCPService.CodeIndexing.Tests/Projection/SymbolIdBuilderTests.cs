using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Projection;
using Xunit;

namespace StewardessMCPService.CodeIndexing.Tests.Projection;

/// <summary>
/// Unit tests for <see cref="SymbolIdBuilder"/>.
/// Verifies stable ID format, repo scope derivation, and kind category mapping.
/// </summary>
public class SymbolIdBuilderTests
{
    // ── BuildSymbolId ────────────────────────────────────────────────────────

    [Fact]
    public void BuildSymbolId_ReturnsExpectedFormat()
    {
        var id = SymbolIdBuilder.BuildSymbolId("csharp", "myrepo", "type", "MyApp.Domain.User");
        Assert.Equal("csharp:myrepo:type:MyApp.Domain.User", id);
    }

    [Fact]
    public void BuildSymbolId_NamespaceKind_UsesNsCategory()
    {
        var id = SymbolIdBuilder.BuildSymbolId("csharp", "myrepo", "ns", "MyApp.Domain");
        Assert.Equal("csharp:myrepo:ns:MyApp.Domain", id);
    }

    // ── BuildSymbolKey ───────────────────────────────────────────────────────

    [Fact]
    public void BuildSymbolKey_ReturnsExpectedPipeDelimitedFormat()
    {
        var key = SymbolIdBuilder.BuildSymbolKey("csharp", "myrepo", "type", "MyApp.Domain.User");
        Assert.Equal("myrepo|csharp|type|MyApp.Domain.User", key);
    }

    // ── DeriveRepoScope ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Repos\MyProject", "myproject")]
    [InlineData(@"C:\Repos\My-Cool_Project", "my-cool-project")]
    [InlineData(@"C:\Repos\MyProject\", "myproject")]
    [InlineData(@"/home/user/repos/stewardess", "stewardess")]
    [InlineData("stewardess", "stewardess")]
    public void DeriveRepoScope_ReturnsLastSegmentLowercaseSanitized(string rootPath, string expected)
    {
        Assert.Equal(expected, SymbolIdBuilder.DeriveRepoScope(rootPath));
    }

    [Fact]
    public void DeriveRepoScope_TrimsTrailingSlash()
    {
        var scope = SymbolIdBuilder.DeriveRepoScope(@"C:\Repos\MyProject\");
        Assert.Equal("myproject", scope);
    }

    // ── GetKindCategory ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(SymbolKind.Namespace, "ns")]
    [InlineData(SymbolKind.Module, "mod")]
    [InlineData(SymbolKind.Package, "mod")]
    [InlineData(SymbolKind.Script, "mod")]
    [InlineData(SymbolKind.Class, "type")]
    [InlineData(SymbolKind.Struct, "type")]
    [InlineData(SymbolKind.Interface, "type")]
    [InlineData(SymbolKind.Enum, "type")]
    [InlineData(SymbolKind.Record, "type")]
    [InlineData(SymbolKind.TypeAlias, "type")]
    [InlineData(SymbolKind.Method, "callable")]
    [InlineData(SymbolKind.Function, "callable")]
    [InlineData(SymbolKind.Constructor, "callable")]
    [InlineData(SymbolKind.Property, "member")]
    [InlineData(SymbolKind.Field, "member")]
    [InlineData(SymbolKind.Event, "member")]
    [InlineData(SymbolKind.Constant, "member")]
    public void GetKindCategory_ReturnsExpectedCategory(SymbolKind kind, string expected)
    {
        Assert.Equal(expected, SymbolIdBuilder.GetKindCategory(kind));
    }

    [Fact]
    public void GetKindCategory_SqlTableKind_ReturnsSym()
    {
        // SQL/document kinds have no specific category and fall through to "sym"
        Assert.Equal("sym", SymbolIdBuilder.GetKindCategory(SymbolKind.Table));
    }
}
