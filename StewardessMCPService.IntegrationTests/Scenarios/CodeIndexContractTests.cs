using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StewardessMCPService.IntegrationTests.Helpers;
using Xunit;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    /// <summary>
    /// Phase 6 integration tests verifying the agent-optimized MCP contract:
    /// structured errors, pagination, confidence fields, the ping tool, and summary mode.
    /// Uses <see cref="ReferenceIndexedFixture"/> which indexes a known Orders.cs file.
    /// </summary>
    [Collection(IntegrationTestCollection.Name)]
    public sealed class CodeIndexContractTests : IClassFixture<ReferenceIndexedFixture>
    {
        private readonly ReferenceIndexedFixture _ref;

        /// <summary>Receives the shared reference fixture injected by xUnit.</summary>
        public CodeIndexContractTests(ReferenceIndexedFixture rf)
        {
            _ref = rf ?? throw new ArgumentNullException(nameof(rf));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static string? Str(JToken token, string key) =>
            (token as JObject)?.GetValue(key, StringComparison.OrdinalIgnoreCase)?.Value<string>();

        private async Task<string> GetSymbolIdAsync(string name)
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.find_symbols",
                new { query_text = name, root_path = _ref.RootPath, match_mode = "exact" });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items!);

            return items![0]
                .Value<JObject>()!
                .GetValue("SymbolId", StringComparison.OrdinalIgnoreCase)!
                .Value<string>()!;
        }

        // ── Structured error contract ─────────────────────────────────────────────

        /// <summary>
        /// Requesting occurrences for an unknown symbol must return a structured
        /// error object with the required <c>Code</c> and <c>Message</c> fields.
        /// </summary>
        [Fact]
        public async Task Error_UnknownSymbol_ReturnsStructuredErrorObject()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_dependencies",
                new { symbol_id = "cs:DoesNotExist:type:DoesNotExist" });

            var errorObj = data.GetValue("error", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(errorObj);

            var code = Str(errorObj!, "Code");
            Assert.NotNull(code);
            Assert.NotEmpty(code!);

            var message = Str(errorObj!, "Message");
            Assert.NotNull(message);
            Assert.NotEmpty(message!);
        }

        /// <summary>
        /// Requesting a file that does not exist must return an error object
        /// with the <c>Code</c> field set to a recognisable error code.
        /// </summary>
        [Fact]
        public async Task Error_UnknownFile_ReturnsStructuredErrorWithCode()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = "NoSuchFile.cs", root_path = _ref.RootPath });

            var errorObj = data.GetValue("error", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(errorObj);

            var code = Str(errorObj!, "Code");
            Assert.False(string.IsNullOrEmpty(code), "error.Code must be a non-empty string");
        }

        /// <summary>
        /// Requesting get_symbol with a missing symbol_id returns a ValidationError code.
        /// </summary>
        [Fact]
        public async Task Error_MissingRequiredParam_ReturnsValidationErrorCode()
        {
            var (data, _) = await _ref.Client.CallToolAsync(
                "code_index.get_references",
                new { });

            var errorObj = data.GetValue("error", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(errorObj);

            var code = Str(errorObj!, "Code");
            Assert.Equal("ValidationError", code, StringComparer.OrdinalIgnoreCase);
        }

        // ── Pagination contract ───────────────────────────────────────────────────

        /// <summary>
        /// Requesting the first page of symbol occurrences must return
        /// pagination metadata: Page, PageSize, TotalItems, HasMore.
        /// </summary>
        [Fact]
        public async Task Pagination_GetSymbolOccurrences_ReturnsPaginationFields()
        {
            var symbolId = await GetSymbolIdAsync(ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol_occurrences",
                new { symbol_id = symbolId, page = 1, page_size = 50 });

            Assert.False(isError);

            var page = data.GetValue("Page", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? -1;
            Assert.Equal(1, page);

            var pageSize = data.GetValue("PageSize", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? -1;
            Assert.Equal(50, pageSize);

            var totalItems = data.GetValue("TotalItems", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? -1;
            Assert.True(totalItems >= 0, $"TotalItems must be non-negative, got {totalItems}");

            // HasMore must be present
            Assert.NotNull(data.GetValue("HasMore", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Requesting page 1 with page_size=1 for a symbol with multiple occurrences
        /// must have HasMore=true and TotalItems greater than the single returned item.
        /// </summary>
        [Fact]
        public async Task Pagination_SmallPageSize_HasMoreIsTrue()
        {
            var symbolId = await GetSymbolIdAsync(ReferenceIndexedFixture.DerivedClassName);

            // First check total with large page
            var (dataAll, _) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol_occurrences",
                new { symbol_id = symbolId, page = 1, page_size = 200 });

            var totalItems = dataAll.GetValue("TotalItems", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;

            if (totalItems <= 1)
                return; // skip: not enough occurrences to paginate

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol_occurrences",
                new { symbol_id = symbolId, page = 1, page_size = 1 });

            Assert.False(isError);
            var hasMore = data.GetValue("HasMore", StringComparison.OrdinalIgnoreCase)?.Value<bool>() ?? false;
            Assert.True(hasMore, "HasMore should be true when there are more pages");

            var occurrences = data.GetValue("Occurrences", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(occurrences);
            Assert.Single(occurrences!);
        }

        /// <summary>
        /// Dependency list must include pagination fields: Page, PageSize, TotalItems, HasMore.
        /// </summary>
        [Fact]
        public async Task Pagination_GetDependencies_ReturnsPaginationFields()
        {
            var symbolId = await GetSymbolIdAsync(ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_dependencies",
                new { symbol_id = symbolId, page = 1, page_size = 50 });

            Assert.False(isError);

            var page = data.GetValue("Page", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? -1;
            Assert.Equal(1, page);

            var totalItems = data.GetValue("TotalItems", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? -1;
            Assert.True(totalItems >= 0, "TotalItems must be non-negative");
        }

        // ── Deterministic serialization ───────────────────────────────────────────

        /// <summary>
        /// Calling the same query twice must return identical JSON output.
        /// </summary>
        [Fact]
        public async Task Deterministic_SameQuery_IdenticalJson()
        {
            var symbolId = await GetSymbolIdAsync(ReferenceIndexedFixture.DerivedClassName);

            var (data1, _) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol",
                new { symbol_id = symbolId });

            var (data2, _) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol",
                new { symbol_id = symbolId });

            Assert.Equal(data1.ToString(), data2.ToString());
        }

        // ── Confidence and provenance ─────────────────────────────────────────────

        /// <summary>
        /// Each dependency in the result must have a <c>ResolutionMethod</c> string
        /// and a <c>Confidence</c> value in [0.0, 1.0].
        /// </summary>
        [Fact]
        public async Task Confidence_Dependencies_HaveResolutionMethodAndConfidence()
        {
            var symbolId = await GetSymbolIdAsync(ReferenceIndexedFixture.DerivedClassName);

            var (data, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_dependencies",
                new { symbol_id = symbolId, hard_only = false, include_confidence = true });

            Assert.False(isError);

            var deps = data.GetValue("Dependencies", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(deps);

            if (deps!.Count == 0) return; // no deps to check

            foreach (var dep in deps)
            {
                var method = Str(dep, "ResolutionMethod");
                Assert.False(string.IsNullOrEmpty(method), "Each dependency must have a ResolutionMethod");

                var conf = (dep as JObject)?.GetValue("Confidence", StringComparison.OrdinalIgnoreCase)?.Value<double>() ?? -1.0;
                Assert.InRange(conf, 0.0, 1.0);
            }
        }

        // ── Ping tool ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The ping tool must return status = "ok" and include "csharp" in the languages array.
        /// </summary>
        [Fact]
        public async Task Ping_ReturnsOkStatusAndLanguages()
        {
            var (data, isError) = await _ref.Client.CallToolAsync("code_index.ping");

            Assert.False(isError);

            var status = Str(data, "status");
            Assert.Equal("ok", status, StringComparer.OrdinalIgnoreCase);

            var languages = data.GetValue("languages", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(languages);
            Assert.NotEmpty(languages!);

            var languageIds = languages!.Select(l => l.Value<string>()).ToArray();
            Assert.Contains("csharp", languageIds, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The ping tool must return a non-empty tool_chains array.
        /// </summary>
        [Fact]
        public async Task Ping_ReturnsToolChains()
        {
            var (data, _) = await _ref.Client.CallToolAsync("code_index.ping");

            var toolChains = data.GetValue("tool_chains", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(toolChains);
            Assert.NotEmpty(toolChains!);
        }

        // ── Summary mode ─────────────────────────────────────────────────────────

        /// <summary>
        /// Calling get_symbol with mode="summary" must return a compact object
        /// without the full Symbol envelope, and a smaller JSON payload than mode="expanded".
        /// </summary>
        [Fact]
        public async Task SummaryMode_GetSymbol_ReturnsCompactResponse()
        {
            var symbolId = await GetSymbolIdAsync(ReferenceIndexedFixture.DerivedClassName);

            var (dataSummary, isErrorSummary) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol",
                new { symbol_id = symbolId, mode = "summary" });

            var (dataExpanded, isErrorExpanded) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol",
                new { symbol_id = symbolId, mode = "expanded" });

            Assert.False(isErrorSummary);
            Assert.False(isErrorExpanded);

            // Summary should NOT have the full Symbol envelope
            var symbolEnvelope = dataSummary.GetValue("Symbol", StringComparison.OrdinalIgnoreCase);
            Assert.Null(symbolEnvelope);

            // Summary should have compact identity fields
            var symbolId2 = Str(dataSummary, "symbolId");
            Assert.NotNull(symbolId2);
            Assert.Equal(symbolId, symbolId2);

            var name = Str(dataSummary, "name");
            Assert.NotNull(name);

            var kind = Str(dataSummary, "kind");
            Assert.NotNull(kind);

            // Summary payload must be smaller than expanded
            Assert.True(
                dataSummary.ToString().Length < dataExpanded.ToString().Length,
                "Summary mode response should be smaller than expanded mode response");
        }

        /// <summary>
        /// Calling get_symbol_relationships with mode="summary" must limit each section to 5 items.
        /// </summary>
        [Fact]
        public async Task SummaryMode_GetSymbolRelationships_LimitsToFiveItems()
        {
            var symbolId = await GetSymbolIdAsync(ReferenceIndexedFixture.DerivedClassName);

            var (dataSummary, isError) = await _ref.Client.CallToolAsync(
                "code_index.get_symbol_relationships",
                new { symbol_id = symbolId, mode = "summary" });

            Assert.False(isError);

            // Each section must have at most 5 items
            foreach (var section in new[] { "Children", "References", "Dependencies", "Dependents" })
            {
                var items = dataSummary.GetValue(section, StringComparison.OrdinalIgnoreCase) as JArray;
                if (items != null)
                    Assert.True(items.Count <= 5, $"Section '{section}' must have at most 5 items in summary mode, got {items.Count}");
            }
        }
    }
}
