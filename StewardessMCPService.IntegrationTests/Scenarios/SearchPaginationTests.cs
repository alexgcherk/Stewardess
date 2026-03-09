using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using StewardessMCPService.IntegrationTests.Helpers;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    [Collection(IntegrationTestCollection.Name)]
    /// <summary>
    /// Integration tests verifying pagination fields (Page, PageSize, TotalItems, HasMore)
    /// on the search MCP tools: search_text, search_regex, search_file_names,
    /// and search_by_extension.
    /// All calls go through <c>POST /mcp/v1/</c> via <see cref="McpRestClient.CallToolAsync"/>.
    /// </summary>
    public sealed class SearchPaginationTests : IDisposable
    {
        private readonly TempTestRepository _tempRepo;
        private readonly McpTestServer      _server;
        private readonly McpRestClient      _client;

        /// <summary>
        /// Creates a temporary repository with enough C# files to exercise
        /// pagination (more than the page sizes used in the tests).
        /// </summary>
        public SearchPaginationTests()
        {
            _tempRepo = new TempTestRepository();

            // Create 12 C# files each containing "using" and "namespace" declarations.
            // This ensures every page-size-limited search query has HasMore=true on page 1.
            for (int i = 0; i < 12; i++)
            {
                File.WriteAllText(
                    Path.Combine(_tempRepo.Root, $"SearchTestFile{i}.cs"),
                    $"using System;\n" +
                    $"using System.Collections.Generic;\n" +
                    $"namespace SearchTestApp.Module{i}\n" +
                    $"{{\n" +
                    $"    public class SearchTestClass{i}\n" +
                    $"    {{\n" +
                    $"        public void SearchTestMethod{i}() {{ }}\n" +
                    $"    }}\n" +
                    $"}}\n");
            }

            _server = new McpTestServer(_tempRepo.Root);
            _client = _server.CreateHttpClient();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _server?.Dispose();
            _tempRepo?.Dispose();
        }

        // ── search_text pagination ────────────────────────────────────────────────

        /// <summary>
        /// search_text with page=1, page_size=5 must return Page=1, PageSize=5,
        /// TotalItems≥0, HasMore present, and at most 5 file results.
        /// </summary>
        [Fact]
        public async Task SearchText_WithPagination_ReturnsPaginationFields()
        {
            var (data, isError) = await _client.CallToolAsync("search_text", new
            {
                query     = "using",
                page      = 1,
                page_size = 5
            });

            Assert.False(isError, $"Tool returned error: {data}");

            var page      = data.GetValue("Page",      StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var pageSize  = data.GetValue("PageSize",  StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var totalItems= data.GetValue("TotalItems",StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var hasMore   = data.GetValue("HasMore",   StringComparison.OrdinalIgnoreCase);
            var files     = data.GetValue("Files",     StringComparison.OrdinalIgnoreCase) as JArray;

            Assert.Equal(1, page);
            Assert.Equal(5, pageSize);
            Assert.True(totalItems.HasValue && totalItems.Value >= 0, "TotalItems must be non-negative");
            Assert.NotNull(hasMore);
            Assert.Equal(JTokenType.Boolean, hasMore.Type);

            if (files != null)
                Assert.True(files.Count <= 5, $"Page contained {files.Count} files but page_size=5");
        }

        /// <summary>
        /// search_text with page_size=2 and then page=2 must return Page=2
        /// when HasMore was true on page 1.
        /// </summary>
        [Fact]
        public async Task SearchText_PageTwo_ReturnsNextPage()
        {
            var (page1Data, page1Error) = await _client.CallToolAsync("search_text", new
            {
                query     = "using",
                page      = 1,
                page_size = 2
            });

            Assert.False(page1Error);

            var hasMore = page1Data
                .GetValue("HasMore", StringComparison.OrdinalIgnoreCase)
                ?.Value<bool>() ?? false;

            if (!hasMore)
            {
                // Not enough results to paginate; skip the rest of the test.
                return;
            }

            var (page2Data, page2Error) = await _client.CallToolAsync("search_text", new
            {
                query     = "using",
                page      = 2,
                page_size = 2
            });

            Assert.False(page2Error);

            var pageNum = page2Data
                .GetValue("Page", StringComparison.OrdinalIgnoreCase)?.Value<int>();
            Assert.Equal(2, pageNum);
        }

        /// <summary>
        /// search_text called without explicit page parameter must return Page=1.
        /// </summary>
        [Fact]
        public async Task SearchText_DefaultPage_IsOne()
        {
            var (data, isError) = await _client.CallToolAsync("search_text", new
            {
                query = "namespace"
            });

            Assert.False(isError);

            var page = data.GetValue("Page", StringComparison.OrdinalIgnoreCase)?.Value<int>();
            Assert.Equal(1, page);
        }

        // ── search_regex pagination ───────────────────────────────────────────────

        /// <summary>
        /// search_regex with page=1, page_size=3 must return the pagination fields.
        /// </summary>
        [Fact]
        public async Task SearchRegex_WithPagination_ReturnsPaginationFields()
        {
            var (data, isError) = await _client.CallToolAsync("search_regex", new
            {
                pattern   = @"class\s+\w+",
                page      = 1,
                page_size = 3
            });

            Assert.False(isError, $"Tool returned error: {data}");

            var page     = data.GetValue("Page",     StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var pageSize = data.GetValue("PageSize", StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var hasMore  = data.GetValue("HasMore",  StringComparison.OrdinalIgnoreCase);
            var total    = data.GetValue("TotalItems",StringComparison.OrdinalIgnoreCase)?.Value<int>();

            Assert.Equal(1, page);
            Assert.Equal(3, pageSize);
            Assert.NotNull(hasMore);
            Assert.Equal(JTokenType.Boolean, hasMore.Type);
            Assert.True(total.HasValue && total.Value >= 0);
        }

        // ── search_file_names pagination ──────────────────────────────────────────

        /// <summary>
        /// search_file_names with page=1, page_size=5 must return Page, PageSize,
        /// and HasMore fields.
        /// </summary>
        [Fact]
        public async Task SearchFileNames_WithPagination_ReturnsPaginationFields()
        {
            var (data, isError) = await _client.CallToolAsync("search_file_names", new
            {
                pattern   = ".cs",
                page      = 1,
                page_size = 5
            });

            Assert.False(isError, $"Tool returned error: {data}");

            var page     = data.GetValue("Page",     StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var pageSize = data.GetValue("PageSize", StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var hasMore  = data.GetValue("HasMore",  StringComparison.OrdinalIgnoreCase);

            Assert.Equal(1, page);
            Assert.Equal(5, pageSize);
            Assert.NotNull(hasMore);
            Assert.Equal(JTokenType.Boolean, hasMore.Type);
        }

        // ── search_by_extension pagination ────────────────────────────────────────

        /// <summary>
        /// search_by_extension with page=1, page_size=5 must return Page, PageSize,
        /// and HasMore fields.
        /// </summary>
        [Fact]
        public async Task SearchByExtension_WithPagination_ReturnsPaginationFields()
        {
            var (data, isError) = await _client.CallToolAsync("search_by_extension", new
            {
                extensions = new[] { ".cs" },
                page       = 1,
                page_size  = 5
            });

            Assert.False(isError, $"Tool returned error: {data}");

            var page     = data.GetValue("Page",     StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var pageSize = data.GetValue("PageSize", StringComparison.OrdinalIgnoreCase)?.Value<int>();
            var hasMore  = data.GetValue("HasMore",  StringComparison.OrdinalIgnoreCase);

            Assert.Equal(1, page);
            Assert.Equal(5, pageSize);
            Assert.NotNull(hasMore);
            Assert.Equal(JTokenType.Boolean, hasMore.Type);
        }

        /// <summary>
        /// search_text called without page_size must return PageSize == 50 (the new default).
        /// </summary>
        [Fact]
        public async Task SearchText_PageSizeZero_IsBoundedTo50Default()
        {
            var (data, isError) = await _client.CallToolAsync("search_text", new
            {
                query = "namespace"
            });

            Assert.False(isError, $"Tool returned error: {data}");

            var pageSize = data.GetValue("PageSize", StringComparison.OrdinalIgnoreCase)?.Value<int>();
            Assert.Equal(50, pageSize);
        }
    }
}
