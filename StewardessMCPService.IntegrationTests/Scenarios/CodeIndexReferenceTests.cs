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
    /// Integration tests for the three Phase 3 MCP tools:
    /// <c>code_index.get_imports</c>, <c>code_index.get_references</c>,
    /// and <c>code_index.get_file_references</c>.
    ///
    /// Uses two fixtures:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="HelloWorldIndexedFixture"/> — for simple baseline cases
    ///     (1 import, 0 reference edges).
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="ReferenceIndexedFixture"/> — for rich reference-edge cases
    ///     (2 imports, 5 edges, known inheritance and object-creation patterns).
    ///   </description></item>
    /// </list>
    /// </summary>
    [Collection(IntegrationTestCollection.Name)]
    public sealed class CodeIndexReferenceTests
        : IClassFixture<HelloWorldIndexedFixture>,
          IClassFixture<ReferenceIndexedFixture>
    {
        private readonly HelloWorldIndexedFixture _hw;
        private readonly ReferenceIndexedFixture  _ref;

        /// <summary>Receives both fixtures injected by xUnit.</summary>
        public CodeIndexReferenceTests(HelloWorldIndexedFixture hw, ReferenceIndexedFixture rf)
        {
            _hw  = hw  ?? throw new ArgumentNullException(nameof(hw));
            _ref = rf  ?? throw new ArgumentNullException(nameof(rf));
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Searches the given index for a symbol with <paramref name="name"/> using
        /// exact match and returns its <c>SymbolId</c>. Asserts the symbol is found.
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

        // ── code_index.get_imports — HelloWorld baseline ──────────────────────────

        /// <summary>
        /// Program.cs has exactly one <c>using</c> directive (<c>using System;</c>),
        /// so the Items array must contain exactly one entry.
        /// </summary>
        [Fact]
        public async Task GetImports_HelloWorld_ReturnsOneItem()
        {
            var (data, isError) = await _hw.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _hw.SampleFilePath, root_path = _hw.RootPath });

            Assert.False(isError);
            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Single(items!);
        }

        /// <summary>
        /// The single import in Program.cs must have <c>Kind = "using"</c>.
        /// </summary>
        [Fact]
        public async Task GetImports_HelloWorld_KindIsUsing()
        {
            var (data, _) = await _hw.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _hw.SampleFilePath, root_path = _hw.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            var kind = Str(items![0], "Kind");
            Assert.Equal("using", kind, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The single import in Program.cs must have <c>NormalizedTarget = "System"</c>.
        /// </summary>
        [Fact]
        public async Task GetImports_HelloWorld_NormalizedTargetIsSystem()
        {
            var (data, _) = await _hw.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _hw.SampleFilePath, root_path = _hw.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            var target = Str(items![0], "NormalizedTarget");
            Assert.Equal("System", target, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Requesting imports for a file that does not exist in the snapshot must
        /// return a response that carries a non-empty <c>error</c> field.
        /// </summary>
        [Fact]
        public async Task GetImports_InvalidFilePath_ResponseHasError()
        {
            var (data, _) = await _hw.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = "DoesNotExist.cs", root_path = _hw.RootPath });

            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(error);
        }

        // ── code_index.get_imports — ReferenceFixture (Orders.cs) ────────────────

        /// <summary>
        /// Orders.cs has exactly two <c>using</c> directives, so Items must have
        /// <see cref="ReferenceIndexedFixture.ImportCount"/> entries.
        /// </summary>
        [Fact]
        public async Task GetImports_Orders_ReturnsTwoItems()
        {
            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _ref.SampleFilePath, root_path = _ref.RootPath });

            Assert.False(isError);
            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Equal(ReferenceIndexedFixture.ImportCount, items!.Count);
        }

        /// <summary>
        /// The first import in Orders.cs is <c>using System;</c>, so NormalizedTarget
        /// must equal "System".
        /// </summary>
        [Fact]
        public async Task GetImports_Orders_FirstTargetIsSystem()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _ref.SampleFilePath, root_path = _ref.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            var target = Str(items![0], "NormalizedTarget");
            Assert.Equal(ReferenceIndexedFixture.FirstImportTarget, target,
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The second import in Orders.cs is <c>using System.Collections.Generic;</c>.
        /// </summary>
        [Fact]
        public async Task GetImports_Orders_SecondTargetIsCollections()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _ref.SampleFilePath, root_path = _ref.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            var target = Str(items![1], "NormalizedTarget");
            Assert.Equal(ReferenceIndexedFixture.SecondImportTarget, target,
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Every import in Orders.cs is a plain <c>using</c> directive (none are
        /// static or aliased), so all entries must report <c>Kind = "using"</c>.
        /// </summary>
        [Fact]
        public async Task GetImports_Orders_AllKindsAreUsing()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _ref.SampleFilePath, root_path = _ref.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.All(items!, item =>
            {
                var kind = Str(item, "Kind");
                Assert.Equal("using", kind, StringComparer.OrdinalIgnoreCase);
            });
        }

        // ── code_index.get_references — HelloWorld baseline ───────────────────────

        /// <summary>
        /// The <c>Program</c> class in HelloWorld has no base class, no fields, and
        /// its sole method returns <c>void</c> (a predefined type that is skipped),
        /// so its outgoing reference list must be empty.
        /// </summary>
        [Fact]
        public async Task GetReferences_ProgramClass_OutgoingIsEmpty()
        {
            var symbolId = await GetSymbolIdAsync(
                _hw.Client, _hw.RootPath, HelloWorldIndexedFixture.ClassName);

            var (data, isError) = await _hw.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = true, include_incoming = false });

            Assert.False(isError);
            var outgoing = data.GetValue("OutgoingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(outgoing);
            Assert.Empty(outgoing!);
        }

        /// <summary>
        /// Querying references with a symbol ID that does not exist in the snapshot
        /// must return a response that carries a non-empty <c>error</c> field.
        /// </summary>
        [Fact]
        public async Task GetReferences_NonExistentSymbol_ResponseHasError()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = "csharp:test:type:SymbolThatDoesNotExist" });

            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(error);
        }

        // ── code_index.get_references — Orders.cs (outgoing from Order) ───────────

        /// <summary>
        /// The <c>Order</c> class has three non-primitive type usages at class scope:
        /// it inherits from <c>BaseOrder</c>, implements <c>IEntity</c>, and declares
        /// a <c>BaseOrder Parent</c> property — so its outgoing count must equal
        /// <see cref="ReferenceIndexedFixture.OrderOutgoingEdgeCount"/>.
        /// </summary>
        [Fact]
        public async Task GetReferences_OrderClass_OutgoingCountIsThree()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = true, include_incoming = false });

            Assert.False(isError);
            var outgoing = data.GetValue("OutgoingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(outgoing);
            Assert.Equal(ReferenceIndexedFixture.OrderOutgoingEdgeCount, outgoing!.Count);
        }

        /// <summary>
        /// One of <c>Order</c>'s outgoing edges must have
        /// <c>RelationshipKind = "Inherits"</c> targeting <c>BaseOrder</c>.
        /// </summary>
        [Fact]
        public async Task GetReferences_OrderClass_OutgoingContainsInheritsEdge()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = true });

            var outgoing = data.GetValue("OutgoingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(outgoing);
            Assert.Contains(outgoing!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Inherits",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// One of <c>Order</c>'s outgoing edges must have
        /// <c>RelationshipKind = "Implements"</c> targeting <c>IEntity</c>.
        /// </summary>
        [Fact]
        public async Task GetReferences_OrderClass_OutgoingContainsImplementsEdge()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = true });

            var outgoing = data.GetValue("OutgoingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(outgoing);
            Assert.Contains(outgoing!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Implements",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// One of <c>Order</c>'s outgoing edges must have
        /// <c>RelationshipKind = "ContainsPropertyOfType"</c> for the <c>Parent</c> property.
        /// </summary>
        [Fact]
        public async Task GetReferences_OrderClass_OutgoingContainsPropertyTypeEdge()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = true });

            var outgoing = data.GetValue("OutgoingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(outgoing);
            Assert.Contains(outgoing!, e =>
                string.Equals(Str(e, "RelationshipKind"), "ContainsPropertyOfType",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// All three outgoing edges from <c>Order</c> must have
        /// <c>ResolutionClass = "ScopedBound"</c> because <c>BaseOrder</c> and
        /// <c>IEntity</c> are both declared in the same snapshot.
        /// </summary>
        [Fact]
        public async Task GetReferences_OrderClass_AllOutgoingEdgesAreScopedBound()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = true });

            var outgoing = data.GetValue("OutgoingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(outgoing);
            Assert.NotEmpty(outgoing!);
            Assert.All(outgoing!, edge =>
            {
                var resClass = Str(edge, "ResolutionClass");
                Assert.Equal("ScopedBound", resClass, StringComparer.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// The <c>Evidence</c> field of <c>Order</c>'s outgoing edges must include
        /// the target type names — confirming <c>BaseOrder</c> (Inherits) and
        /// <c>IEntity</c> (Implements) are present.
        /// </summary>
        [Fact]
        public async Task GetReferences_OrderClass_EvidenceIncludesBaseTypeNames()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.DerivedClassName);

            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = true });

            var outgoing = data.GetValue("OutgoingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(outgoing);
            var evidenceValues = outgoing!
                .Select(e => Str(e, "Evidence"))
                .Where(v => v != null)
                .ToList();

            Assert.Contains(ReferenceIndexedFixture.BaseClassName,  evidenceValues);
            Assert.Contains(ReferenceIndexedFixture.InterfaceName,  evidenceValues);
        }

        // ── code_index.get_references — Orders.cs (incoming to BaseOrder) ─────────

        /// <summary>
        /// <c>BaseOrder</c> is referenced by four edges in total: it is inherited by
        /// <c>Order</c>, used as a property type in <c>Order</c>, returned by
        /// <c>GetParent</c>, and instantiated inside <c>GetParent</c>.
        /// </summary>
        [Fact]
        public async Task GetReferences_BaseOrderClass_IncomingCountIsFour()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.BaseClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = false, include_incoming = true });

            Assert.False(isError);
            var incoming = data.GetValue("IncomingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(incoming);
            Assert.Equal(ReferenceIndexedFixture.BaseOrderIncomingEdgeCount, incoming!.Count);
        }

        /// <summary>
        /// The incoming references to <c>BaseOrder</c> must include a
        /// <c>CreatesInstanceOf</c> edge, originating from the <c>new BaseOrder()</c>
        /// expression in <c>Order.GetParent</c>.
        /// </summary>
        [Fact]
        public async Task GetReferences_BaseOrderClass_IncomingContainsCreatesInstanceOf()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.BaseClassName);

            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = false, include_incoming = true });

            var incoming = data.GetValue("IncomingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(incoming);
            Assert.Contains(incoming!, e =>
                string.Equals(Str(e, "RelationshipKind"), "CreatesInstanceOf",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The incoming references to <c>BaseOrder</c> must include a
        /// <c>ReturnsType</c> edge, originating from the <c>GetParent</c> method.
        /// </summary>
        [Fact]
        public async Task GetReferences_BaseOrderClass_IncomingContainsReturnsType()
        {
            var symbolId = await GetSymbolIdAsync(
                _ref.Client, _ref.RootPath, ReferenceIndexedFixture.BaseClassName);

            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = symbolId, include_outgoing = false, include_incoming = true });

            var incoming = data.GetValue("IncomingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(incoming);
            Assert.Contains(incoming!, e =>
                string.Equals(Str(e, "RelationshipKind"), "ReturnsType",
                    StringComparison.OrdinalIgnoreCase));
        }

        // ── code_index.get_file_references — HelloWorld baseline ──────────────────

        /// <summary>
        /// Program.cs declares only the <c>Program</c> class, whose method returns
        /// <c>void</c> and takes no non-primitive parameters, so there must be
        /// zero reference edges originating from it.
        /// </summary>
        [Fact]
        public async Task GetFileReferences_HelloWorld_ReturnsNoEdges()
        {
            var (data, isError) = await _hw.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _hw.SampleFilePath });

            Assert.False(isError);
            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Empty(items!);
        }

        // ── code_index.get_file_references — Orders.cs ────────────────────────────

        /// <summary>
        /// Orders.cs produces exactly <see cref="ReferenceIndexedFixture.ExpectedEdgeCount"/>
        /// reference edges, so the Items array must have that many entries.
        /// </summary>
        [Fact]
        public async Task GetFileReferences_Orders_ReturnsFiveEdges()
        {
            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _ref.SampleFilePath });

            Assert.False(isError);
            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Equal(ReferenceIndexedFixture.ExpectedEdgeCount, items!.Count);
        }

        /// <summary>
        /// At least one item in the file references of Orders.cs must be an
        /// <c>Inherits</c> edge (from <c>Order</c> to <c>BaseOrder</c>).
        /// </summary>
        [Fact]
        public async Task GetFileReferences_Orders_ContainsInheritsEdge()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _ref.SampleFilePath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Contains(items!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Inherits",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// At least one item in the file references of Orders.cs must be an
        /// <c>Implements</c> edge (from <c>Order</c> to <c>IEntity</c>).
        /// </summary>
        [Fact]
        public async Task GetFileReferences_Orders_ContainsImplementsEdge()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _ref.SampleFilePath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Contains(items!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Implements",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// At least one item in the file references of Orders.cs must be a
        /// <c>CreatesInstanceOf</c> edge (from <c>Order.GetParent</c> to <c>BaseOrder</c>).
        /// </summary>
        [Fact]
        public async Task GetFileReferences_Orders_ContainsCreatesInstanceOf()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _ref.SampleFilePath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Contains(items!, e =>
                string.Equals(Str(e, "RelationshipKind"), "CreatesInstanceOf",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Every reference edge in Orders.cs must carry a non-null, non-empty
        /// <c>Evidence</c> field (the source text of the referenced type name).
        /// </summary>
        [Fact]
        public async Task GetFileReferences_Orders_AllEdgesHaveEvidence()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _ref.SampleFilePath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items!);
            Assert.All(items!, item =>
            {
                var evidence = Str(item, "Evidence");
                Assert.NotNull(evidence);
                Assert.NotEmpty(evidence);
            });
        }

        /// <summary>
        /// Requesting file references for a file path not present in the snapshot
        /// must return a response with a non-empty <c>error</c> field.
        /// </summary>
        [Fact]
        public async Task GetFileReferences_InvalidFilePath_ResponseHasError()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = "DoesNotExist.cs" });

            Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
        }
    }
}
