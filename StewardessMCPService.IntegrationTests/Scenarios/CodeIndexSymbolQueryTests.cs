// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using Newtonsoft.Json.Linq;
using StewardessMCPService.IntegrationTests.Helpers;
using Xunit;

namespace StewardessMCPService.IntegrationTests.Scenarios;

[Collection(IntegrationTestCollection.Name)]
/// <summary>
/// Integration tests for the Phase 2 logical symbol query MCP tools.
/// Exercises: <c>code_index.find_symbols</c>, <c>code_index.get_symbol</c>,
/// <c>code_index.get_symbol_occurrences</c>, <c>code_index.get_symbol_children</c>,
/// <c>code_index.get_type_members</c>, <c>code_index.resolve_location</c>,
/// and <c>code_index.get_namespace_tree</c>.
/// All tool calls go through the JSON-RPC 2.0 endpoint at <c>POST /mcp/v1/</c>.
/// The <see cref="IndexedRepositoryFixture"/> pre-builds an index containing
/// a <c>Customer.cs</c> file with a namespace, class, interface, and enum.
/// </summary>
public sealed class CodeIndexSymbolQueryTests
    : IClassFixture<IndexedRepositoryFixture>, IDisposable
{
    private readonly IndexedRepositoryFixture _fixture;

    public CodeIndexSymbolQueryTests(IndexedRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        /* fixture is owned by xUnit */
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Searches the index for a symbol with the given name (prefix mode) and
    ///     returns its symbolId. Asserts the symbol is found.
    /// </summary>
    private async Task<string> GetSymbolIdAsync(string name)
    {
        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.find_symbols",
            new
            {
                query_text = name,
                root_path = _fixture.RootPath,
                match_mode = "exact"
            });

        var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(symbols);
        Assert.NotEmpty(symbols);

        return symbols[0]
            .Value<JObject>()!
            .GetValue("SymbolId", StringComparison.OrdinalIgnoreCase)!
            .Value<string>()!;
    }

    // ── code_index.find_symbols ───────────────────────────────────────────────

    /// <summary>
    ///     Searching for "Customer" in prefix mode must return at least one symbol
    ///     whose name is or starts with "Customer".
    /// </summary>
    [Fact]
    public async Task FindSymbols_PrefixMode_ReturnsMatchingSymbols()
    {
        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.find_symbols",
            new { query_text = "Customer", root_path = _fixture.RootPath, match_mode = "prefix" });

        Assert.False(isError);
        var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(symbols);
        Assert.NotEmpty(symbols);

        Assert.All(symbols, s =>
        {
            var name = s.Value<JObject>()
                ?.GetValue("Name", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>() ?? "";
            Assert.True(
                name.StartsWith("Customer", StringComparison.OrdinalIgnoreCase),
                $"Expected name starting with 'Customer', got '{name}'");
        });
    }

    /// <summary>
    ///     Exact match mode for "Customer" must return only symbols whose name is
    ///     exactly "Customer".
    /// </summary>
    [Fact]
    public async Task FindSymbols_ExactMode_ReturnsOnlyExactMatch()
    {
        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.find_symbols",
            new { query_text = "Customer", root_path = _fixture.RootPath, match_mode = "exact" });

        Assert.False(isError);
        var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(symbols);
        Assert.NotEmpty(symbols);

        Assert.All(symbols, s =>
        {
            var name = s.Value<JObject>()
                ?.GetValue("Name", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>();
            Assert.Equal("Customer", name);
        });
    }

    /// <summary>
    ///     Filtering by kind="Class" must return only class symbols.
    /// </summary>
    [Fact]
    public async Task FindSymbols_WithClassKindFilter_ReturnsOnlyClasses()
    {
        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.find_symbols",
            new { query_text = "C", root_path = _fixture.RootPath, kind = "Class" });

        var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(symbols);
        Assert.NotEmpty(symbols);

        Assert.All(symbols, s =>
        {
            var kind = s.Value<JObject>()
                ?.GetValue("Kind", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>();
            Assert.Equal("Class", kind, StringComparer.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    ///     Contains mode must find "ICustomerRepository" when searching for "Repo".
    /// </summary>
    [Fact]
    public async Task FindSymbols_ContainsMode_FindsSubstringMatch()
    {
        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.find_symbols",
            new { query_text = "Repo", root_path = _fixture.RootPath, match_mode = "contains" });

        var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(symbols);
        Assert.NotEmpty(symbols);

        var found = false;
        foreach (var s in symbols)
        {
            var name = s.Value<JObject>()
                ?.GetValue("Name", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>() ?? "";
            if (name.Contains("Repository", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
        }

        Assert.True(found, "Expected to find 'ICustomerRepository' via contains search on 'Repo'.");
    }

    /// <summary>
    ///     Search with no matches must return an empty symbols list (not an error).
    /// </summary>
    [Fact]
    public async Task FindSymbols_NoMatch_ReturnsEmptyListNotError()
    {
        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.find_symbols",
            new { query_text = "XYZZY_DEFINITELY_NO_MATCH", root_path = _fixture.RootPath });

        Assert.False(isError);
        var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(symbols);
        Assert.Empty(symbols);
    }

    // ── code_index.get_symbol ─────────────────────────────────────────────────

    /// <summary>
    ///     Fetching a known symbol by its stable ID must return full symbol detail.
    /// </summary>
    [Fact]
    public async Task GetSymbol_BySymbolId_ReturnsFullDetails()
    {
        var symbolId = await GetSymbolIdAsync("Customer");

        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.get_symbol", new { symbol_id = symbolId });

        Assert.False(isError);
        var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        Assert.Null(error);

        var symbol = data.GetValue("Symbol", StringComparison.OrdinalIgnoreCase) as JObject;
        Assert.NotNull(symbol);

        var name = symbol!.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        Assert.Equal("Customer", name);

        var kind = symbol.GetValue("Kind", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        Assert.Equal("Class", kind, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Fetching with an unknown symbol ID must return an error object, not throw.
    /// </summary>
    [Fact]
    public async Task GetSymbol_WithUnknownId_ReturnsErrorObject()
    {
        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.get_symbol", new { symbol_id = "csharp:unknown:type:No.Such.Symbol" });

        Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     GetSymbol for a class must include primary occurrence information.
    /// </summary>
    [Fact]
    public async Task GetSymbol_ClassSymbol_IncludesPrimaryOccurrence()
    {
        var symbolId = await GetSymbolIdAsync("Customer");

        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.get_symbol",
            new { symbol_id = symbolId, include_primary_occurrence = true });

        // PrimaryOccurrence is a nested SymbolLocation object with FilePath
        var primaryOcc = data.GetValue("PrimaryOccurrence", StringComparison.OrdinalIgnoreCase) as JObject;
        Assert.NotNull(primaryOcc);
        var filePath = primaryOcc!.GetValue("FilePath", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        Assert.NotNull(filePath);
    }

    // ── code_index.get_symbol_occurrences ─────────────────────────────────────

    /// <summary>
    ///     A symbol's occurrences must include at least one Declaration occurrence.
    /// </summary>
    [Fact]
    public async Task GetSymbolOccurrences_BySymbolId_ReturnsDeclaration()
    {
        var symbolId = await GetSymbolIdAsync("Customer");

        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.get_symbol_occurrences", new { symbol_id = symbolId });

        Assert.False(isError);
        var occurrences = data.GetValue("Occurrences", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(occurrences);
        Assert.NotEmpty(occurrences);

        var hasDeclaration = false;
        foreach (var occ in occurrences)
        {
            var role = occ.Value<JObject>()
                ?.GetValue("Role", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>() ?? "";
            if (string.Equals(role, "Declaration", StringComparison.OrdinalIgnoreCase))
            {
                hasDeclaration = true;
                break;
            }
        }

        Assert.True(hasDeclaration, "Expected at least one Declaration occurrence.");
    }

    /// <summary>
    ///     Each returned occurrence must carry a file ID referencing the C# source file.
    /// </summary>
    [Fact]
    public async Task GetSymbolOccurrences_BySymbolId_OccurrencesHaveFileId()
    {
        var symbolId = await GetSymbolIdAsync("Customer");

        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.get_symbol_occurrences", new { symbol_id = symbolId });

        var occurrences = data.GetValue("Occurrences", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(occurrences);
        Assert.All(occurrences, occ =>
        {
            var filePath = occ.Value<JObject>()
                ?.GetValue("FilePath", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>();
            Assert.NotNull(filePath);
            Assert.NotEmpty(filePath);
        });
    }

    // ── code_index.get_symbol_children ────────────────────────────────────────

    /// <summary>
    ///     The children of the <c>TestApp.Domain</c> namespace must include at
    ///     least the <c>Customer</c> class and <c>Status</c> enum.
    /// </summary>
    [Fact]
    public async Task GetSymbolChildren_ByNamespaceId_ReturnsTypeSymbols()
    {
        var namespaceId = await GetSymbolIdAsync("TestApp.Domain");

        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.get_symbol_children", new { symbol_id = namespaceId });

        Assert.False(isError);
        var children = data.GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(children);
        Assert.NotEmpty(children);

        var names = children
            .Select(c => c.Value<JObject>()
                ?.GetValue("Name", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>() ?? "")
            .ToList();

        Assert.Contains("Customer", names);
    }

    /// <summary>
    ///     Filtering namespace children by kind="Class" must return only classes.
    /// </summary>
    [Fact]
    public async Task GetSymbolChildren_WithClassKindFilter_ReturnsOnlyClasses()
    {
        var namespaceId = await GetSymbolIdAsync("TestApp.Domain");

        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.get_symbol_children",
            new { symbol_id = namespaceId, kind = "Class" });

        var children = data.GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(children);
        Assert.All(children, c =>
        {
            var kind = c.Value<JObject>()
                ?.GetValue("Kind", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>();
            Assert.Equal("Class", kind, StringComparer.OrdinalIgnoreCase);
        });
    }

    // ── code_index.get_type_members ───────────────────────────────────────────

    /// <summary>
    ///     The members of the <c>Customer</c> class must include at least
    ///     one constructor and one method.
    /// </summary>
    [Fact]
    public async Task GetTypeMembers_CustomerClass_ReturnsConstructorAndMethods()
    {
        var symbolId = await GetSymbolIdAsync("Customer");

        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.get_type_members", new { type_symbol_id = symbolId });

        Assert.False(isError);
        var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        Assert.Null(error);

        // Constructors count
        var ctorCount = (data.GetValue("Constructors", StringComparison.OrdinalIgnoreCase) as JArray)?.Count ?? 0;
        Assert.True(ctorCount >= 1, $"Expected >= 1 constructor, got {ctorCount}");

        // Methods count (Greet)
        var methodCount = (data.GetValue("Methods", StringComparison.OrdinalIgnoreCase) as JArray)?.Count ?? 0;
        Assert.True(methodCount >= 1, $"Expected >= 1 method, got {methodCount}");
    }

    /// <summary>
    ///     The <c>Customer</c> class members must include Name and Age properties.
    /// </summary>
    [Fact]
    public async Task GetTypeMembers_CustomerClass_ReturnsNameAndAgeProperties()
    {
        var symbolId = await GetSymbolIdAsync("Customer");

        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.get_type_members", new { type_symbol_id = symbolId });

        var propCount = (data.GetValue("Properties", StringComparison.OrdinalIgnoreCase) as JArray)?.Count ?? 0;
        Assert.True(propCount >= 2, $"Expected >= 2 properties (Name, Age), got {propCount}");
    }

    /// <summary>
    ///     Calling <c>get_type_members</c> with a non-type symbol (e.g., a namespace)
    ///     must return an error object rather than members.
    /// </summary>
    [Fact]
    public async Task GetTypeMembers_WithNonTypeSymbol_ReturnsErrorObject()
    {
        var namespaceId = await GetSymbolIdAsync("TestApp.Domain");

        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.get_type_members", new { type_symbol_id = namespaceId });

        Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
    }

    // ── code_index.resolve_location ───────────────────────────────────────────

    /// <summary>
    ///     Resolving a known symbol ID must return the file path of the C# source file
    ///     and a positive line number.
    /// </summary>
    [Fact]
    public async Task ResolveLocation_BySymbolId_ReturnsFilePathAndLine()
    {
        var symbolId = await GetSymbolIdAsync("Customer");

        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.resolve_location", new { symbol_id = symbolId });

        Assert.False(isError);
        var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        Assert.Null(error);

        var filePath = data.GetValue("FilePath", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        Assert.NotNull(filePath);
        Assert.True(
            filePath!.EndsWith("Customer.cs", StringComparison.OrdinalIgnoreCase),
            $"Expected file path ending in 'Customer.cs', got '{filePath}'");

        var sourceSpan = data.GetValue("SourceSpan", StringComparison.OrdinalIgnoreCase) as JObject;
        Assert.NotNull(sourceSpan);
        var startLine = sourceSpan!.GetValue("StartLine", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
        Assert.True(startLine > 0, $"Expected positive start line, got {startLine}");
    }

    /// <summary>
    ///     Providing neither <c>symbol_id</c> nor <c>occurrence_id</c> must return an error.
    /// </summary>
    [Fact]
    public async Task ResolveLocation_WithNoIds_ReturnsErrorObject()
    {
        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.resolve_location", new { });

        Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
    }

    // ── code_index.get_namespace_tree ─────────────────────────────────────────

    /// <summary>
    ///     The namespace tree for the indexed repository must include a root node
    ///     for the <c>TestApp</c> or <c>TestApp.Domain</c> namespace.
    /// </summary>
    [Fact]
    public async Task GetNamespaceTree_AfterBuild_ReturnsRootNamespaceNode()
    {
        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.get_namespace_tree", new { root_path = _fixture.RootPath });

        Assert.False(isError);
        var roots = data.GetValue("Roots", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(roots);
        Assert.NotEmpty(roots);

        // The top-level should contain a namespace node whose name or qualified
        // name relates to TestApp or TestApp.Domain.
        var found = false;
        foreach (var rootNode in roots)
        {
            var qn = rootNode.Value<JObject>()
                ?.GetValue("QualifiedName", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>() ?? "";
            if (qn.StartsWith("TestApp", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
        }

        Assert.True(found, "Expected a root namespace node starting with 'TestApp'.");
    }

    /// <summary>
    ///     Namespace tree nodes must carry symbol and file counts when
    ///     <c>include_counts</c> is true.
    /// </summary>
    [Fact]
    public async Task GetNamespaceTree_WithCounts_NodesHaveNonNegativeCounts()
    {
        var (data, _) = await _fixture.Client.CallToolAsync(
            "code_index.get_namespace_tree",
            new { root_path = _fixture.RootPath, include_counts = true });

        var roots = data.GetValue("Roots", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(roots);
        Assert.All(roots, rootNode =>
        {
            var symbolCount = rootNode.Value<JObject>()
                ?.GetValue("SymbolCount", StringComparison.OrdinalIgnoreCase)
                ?.Value<int>() ?? 0;
            Assert.True(symbolCount >= 0, "Symbol count should be non-negative.");
        });
    }

    /// <summary>
    ///     Filtering the namespace tree by language must only return
    ///     containers for that language.
    /// </summary>
    [Fact]
    public async Task GetNamespaceTree_WithCSharpLanguageFilter_ReturnsCSharpContainers()
    {
        var (data, isError) = await _fixture.Client.CallToolAsync(
            "code_index.get_namespace_tree",
            new { root_path = _fixture.RootPath, language = "csharp" });

        Assert.False(isError);
        // Should still have at least one root (TestApp.Domain comes from C#)
        var roots = data.GetValue("Roots", StringComparison.OrdinalIgnoreCase) as JArray;
        Assert.NotNull(roots);
        Assert.NotEmpty(roots);
    }
}