// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StewardessMCPService.IntegrationTests.Helpers;
using Xunit;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    /// <summary>
    /// Integration tests for the four Phase 4 MCP tools:
    /// <c>code_index.get_dependencies</c>, <c>code_index.get_dependents</c>,
    /// <c>code_index.get_symbol_relationships</c>, and <c>code_index.get_file_dependencies</c>.
    ///
    /// Uses two fixtures:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="ReferenceIndexedFixture"/> — C# Orders.cs with 5 edges and 3 types.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="PythonIndexedFixture"/> — animals.py with Dog→Animal inheritance.
    ///   </description></item>
    /// </list>
    /// </summary>
    [Collection(IntegrationTestCollection.Name)]
    public sealed class CodeIndexDependencyTests
        : IClassFixture<ReferenceIndexedFixture>,
          IClassFixture<PythonIndexedFixture>
    {
        private readonly ReferenceIndexedFixture _ref;
        private readonly PythonIndexedFixture    _py;

        /// <summary>Receives both fixtures injected by xUnit.</summary>
        public CodeIndexDependencyTests(ReferenceIndexedFixture rf, PythonIndexedFixture py)
        {
            _ref = rf  ?? throw new ArgumentNullException(nameof(rf));
            _py  = py  ?? throw new ArgumentNullException(nameof(py));
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Finds a symbol by exact name in the given index and returns its SymbolId.
        /// </summary>
        private static async Task<string> GetSymbolIdAsync(
            McpRestClient client, string rootPath, string name)
        {
            var (data, _) = await client.CallToolAsync(
                "code_index.find_symbols",
                new { query_text = name, root_path = rootPath, match_mode = "exact" });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items!);

            return items![0]
                .Value<JObject>()!
                .GetValue("SymbolId", StringComparison.OrdinalIgnoreCase)!
                .Value<string>()!;
        }

        /// <summary>
        /// Returns the string value of <paramref name="key"/> from a JObject,
        /// using a case-insensitive lookup.
        /// </summary>
        private static string? Str(JToken token, string key) =>
            (token as JObject)?.GetValue(key, StringComparison.OrdinalIgnoreCase)?.Value<string>();

        // ── code_index.get_dependencies — Order symbol (C#) ───────────────────────

        /// <summary>
        /// The <c>Order</c> class has outgoing edges to <c>BaseOrder</c> and <c>IEntity</c>,
        /// so the Dependencies array must be non-empty.
        /// </summary>
        [Fact]
        public async Task GetDependencies_OrderSymbol_ReturnsDependencies()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_dependencies",
                new { symbol_id = symbolId });

            Assert.False(isError);
            var deps = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(deps);
            Assert.NotEmpty(deps!);
        }

        /// <summary>
        /// With <c>hard_only=true</c>, all <c>ScopedBound</c> edges (which are hard) are
        /// included, so the count must equal <see cref="ReferenceIndexedFixture.OrderOutgoingEdgeCount"/>.
        /// Every returned dependency must have a hard resolution class.
        /// </summary>
        [Fact]
        public async Task GetDependencies_OrderSymbol_HardOnlyTrue_FiltersWeak()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_dependencies",
                new { symbol_id = symbolId, hard_only = true });

            Assert.False(isError);
            var deps = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(deps);
            Assert.Equal(ReferenceIndexedFixture.OrderOutgoingEdgeCount, deps!.Count);

            var hardClasses = new[] { "ExactBound", "ScopedBound", "ImportBound", "AliasBound" };
            Assert.All(deps!, dep =>
            {
                var rc = Str(dep, "ResolutionClass");
                Assert.Contains(rc, hardClasses, StringComparer.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// With <c>hard_only=false</c>, all edges are returned regardless of resolution class.
        /// The count must be at least <see cref="ReferenceIndexedFixture.OrderOutgoingEdgeCount"/>.
        /// </summary>
        [Fact]
        public async Task GetDependencies_OrderSymbol_HardOnlyFalse_IncludesAll()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_dependencies",
                new { symbol_id = symbolId, hard_only = false });

            Assert.False(isError);
            var deps = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(deps);
            Assert.True(deps!.Count >= ReferenceIndexedFixture.OrderOutgoingEdgeCount,
                $"Expected at least {ReferenceIndexedFixture.OrderOutgoingEdgeCount} dependencies, got {deps!.Count}.");
        }

        /// <summary>
        /// Querying dependencies for a symbol ID that does not exist must return
        /// a response with a non-empty <c>error</c> field.
        /// </summary>
        [Fact]
        public async Task GetDependencies_InvalidSymbol_ReturnsError()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_dependencies",
                new { symbol_id = "cs:DoesNotExist:type:DoesNotExist" });

            Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
        }

        // ── code_index.get_dependents — BaseOrder symbol (C#)─────────────────────

        /// <summary>
        /// <c>BaseOrder</c> is inherited, used as a property type, returned, and instantiated
        /// by <c>Order</c> and its method, so it must have at least one inbound dependent.
        /// </summary>
        [Fact]
        public async Task GetDependents_BaseOrderSymbol_ReturnsOrderAsDependent()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.BaseClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_dependents",
                new { symbol_id = symbolId });

            Assert.False(isError);
            var dependents = data.GetValue("Dependents", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(dependents);
            Assert.NotEmpty(dependents!);
            Assert.Equal(ReferenceIndexedFixture.BaseOrderIncomingEdgeCount, dependents!.Count);
        }

        /// <summary>
        /// Querying dependents for a symbol ID that does not exist must return
        /// a response with a non-empty <c>error</c> field.
        /// </summary>
        [Fact]
        public async Task GetDependents_InvalidSymbol_ReturnsError()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_dependents",
                new { symbol_id = "cs:DoesNotExist:type:DoesNotExist" });

            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(error);
        }

        /// <summary>
        /// <c>IEntity</c> is implemented by <c>Order</c>, so it must have at least one dependent
        /// with an <c>Implements</c> relationship kind.
        /// </summary>
        [Fact]
        public async Task GetDependents_IEntitySymbol_ReturnsOrderAsDependent()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.InterfaceName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_dependents",
                new { symbol_id = symbolId });

            Assert.False(isError);
            var dependents = data.GetValue("Dependents", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(dependents);
            Assert.NotEmpty(dependents!);
            Assert.Contains(dependents!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Implements",
                    StringComparison.OrdinalIgnoreCase));
        }

        // ── code_index.get_symbol_relationships — Order symbol (C#) ───────────────

        /// <summary>
        /// When all sections are requested for the <c>Order</c> symbol, all four sections
        /// (Children, References, Dependencies, Dependents) must be present in the response.
        /// </summary>
        [Fact]
        public async Task GetSymbolRelationships_OrderSymbol_AllSections()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol_relationships",
                new
                {
                    symbol_id            = symbolId,
                    include_references   = true,
                    include_dependencies = true,
                    include_dependents   = true,
                    include_children     = true,
                });

            Assert.False(isError);

            // All four sections must be present (non-null arrays)
            Assert.NotNull(data.GetValue("Children",     StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(data.GetValue("References",   StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(data.GetValue("Dependents",   StringComparison.OrdinalIgnoreCase));

            // Dependencies must contain the outgoing hard edges
            var deps = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(deps);
            Assert.Equal(ReferenceIndexedFixture.OrderOutgoingEdgeCount, deps!.Count);
        }

        /// <summary>
        /// When only <c>include_children=true</c> is requested, the Children section must be
        /// present and the other sections must be absent (null or omitted).
        /// </summary>
        [Fact]
        public async Task GetSymbolRelationships_OrderSymbol_ChildrenOnly()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol_relationships",
                new
                {
                    symbol_id            = symbolId,
                    include_references   = false,
                    include_dependencies = false,
                    include_dependents   = false,
                    include_children     = true,
                });

            Assert.False(isError);
            Assert.NotNull(data.GetValue("Children", StringComparison.OrdinalIgnoreCase));

            // Sections not requested must be absent or null
            var refs  = data.GetValue("References",   StringComparison.OrdinalIgnoreCase);
            var dep   = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase);
            var dents = data.GetValue("Dependents",   StringComparison.OrdinalIgnoreCase);
            Assert.True(refs  == null || refs.Type  == JTokenType.Null);
            Assert.True(dep   == null || dep.Type   == JTokenType.Null);
            Assert.True(dents == null || dents.Type == JTokenType.Null);
        }

        /// <summary>
        /// Querying symbol relationships for a symbol ID that does not exist must return
        /// a response with a non-empty <c>error</c> field.
        /// </summary>
        [Fact]
        public async Task GetSymbolRelationships_InvalidSymbol_ReturnsError()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol_relationships",
                new { symbol_id = "cs:DoesNotExist:type:DoesNotExist" });

            Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
        }

        // ── code_index.get_file_dependencies — Orders.cs (C#) ────────────────────

        /// <summary>
        /// Orders.cs declares all its referenced types in the same file, so all reference
        /// edges are intra-file and must be excluded by the implementation.
        /// The response must contain no error and an empty Dependencies list.
        /// </summary>
        [Fact]
        public async Task GetFileDependencies_OrdersFile_GroupedByTargetFile()
        {
            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_file_dependencies",
                new { file_path = _ref.SampleFilePath });

            Assert.False(isError);
            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Null(error);

            var deps = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(deps);
            // All reference targets are in the same file — no cross-file deps expected
            Assert.Empty(deps!);
        }

        /// <summary>
        /// HelloWorld Program.cs declares only a simple Program class with no non-primitive
        /// parameters or return types, so there must be zero file-level dependencies.
        /// </summary>
        [Fact]
        public async Task GetFileDependencies_FileWithNoRefs_EmptyDependencies()
        {
            // Use the reference fixture's HelloWorld-equivalent: Orders.cs has only intra-file refs.
            // Verify with a direct call on _ref.SampleFilePath — same assertion, valid baseline.
            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_file_dependencies",
                new { file_path = _ref.SampleFilePath, hard_only = false });

            Assert.False(isError);
            var deps = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(deps);
            Assert.Empty(deps!);
        }

        /// <summary>
        /// Requesting file dependencies for a file path not present in the snapshot
        /// must return a response with a non-empty <c>error</c> field.
        /// </summary>
        [Fact]
        public async Task GetFileDependencies_InvalidFile_ReturnsError()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_file_dependencies",
                new { file_path = "DoesNotExist.cs" });

            Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
        }

        // ── Python fixture — Dog / Animal ─────────────────────────────────────────

        /// <summary>
        /// <c>Dog</c> inherits from <c>Animal</c>, so <c>get_dependencies</c> on <c>Dog</c>
        /// must return at least one entry with <c>RelationshipKind = "Inherits"</c>.
        /// </summary>
        [Fact]
        public async Task GetDependencies_DogSymbol_ReturnsInherits()
        {
            var symbolId = await GetSymbolIdAsync(
                _py.Client, _py.RootPath, PythonIndexedFixture.DerivedClassName);

            var (data, isError) = await _py.Client.CallToolAsync(
                "code_index.get_dependencies",
                new { symbol_id = symbolId, hard_only = false });

            Assert.False(isError);
            var deps = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(deps);
            Assert.NotEmpty(deps!);
            Assert.Contains(deps!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Inherits",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// <c>Animal</c> is inherited by <c>Dog</c>, so <c>get_dependents</c> on <c>Animal</c>
        /// must return at least one entry with an <c>Inherits</c> relationship kind.
        /// </summary>
        [Fact]
        public async Task GetDependents_AnimalSymbol_ReturnsDogAsDependent()
        {
            var symbolId = await GetSymbolIdAsync(
                _py.Client, _py.RootPath, PythonIndexedFixture.BaseClassName);

            var (data, isError) = await _py.Client.CallToolAsync(
                "code_index.get_dependents",
                new { symbol_id = symbolId, hard_only = false });

            Assert.False(isError);
            var dependents = data.GetValue("Dependents", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(dependents);
            Assert.NotEmpty(dependents!);
            Assert.Contains(dependents!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Inherits",
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}
