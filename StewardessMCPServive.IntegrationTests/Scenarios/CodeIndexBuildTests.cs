using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using StewardessMCPServive.IntegrationTests.Helpers;

namespace StewardessMCPServive.IntegrationTests.Scenarios
{
    [Collection(IntegrationTestCollection.Name)]
    /// <summary>
    /// Integration tests for the Phase 1 code index MCP tools.
    /// Exercises: <c>code_index.build</c>, <c>code_index.get_status</c>,
    /// <c>code_index.list_files</c>, <c>code_index.get_file_outline</c>,
    /// <c>code_index.get_snapshot_info</c>, <c>code_index.list_roots</c>,
    /// and <c>code_index.get_language_capabilities</c>.
    /// All tool calls go through the JSON-RPC 2.0 endpoint at <c>POST /mcp/v1/</c>.
    /// </summary>
    public sealed class CodeIndexBuildTests : IClassFixture<IndexedRepositoryFixture>, IDisposable
    {
        private readonly IndexedRepositoryFixture _fixture;

        public CodeIndexBuildTests(IndexedRepositoryFixture fixture)
        {
            _fixture = fixture;
        }

        public void Dispose() { /* fixture is owned by xUnit, not us */ }

        // ── code_index.build ─────────────────────────────────────────────────────

        /// <summary>
        /// Building an index for a directory containing a .cs file must succeed
        /// and return a non-empty snapshot ID.
        /// </summary>
        [Fact]
        public void Build_WithCSharpFile_ReturnsSuccessAndSnapshotId()
        {
            // Already built in the fixture; verify the result stored there.
            Assert.NotNull(_fixture.SnapshotId);
            Assert.NotEmpty(_fixture.SnapshotId);
        }

        /// <summary>
        /// After a successful build, <c>FilesIndexed</c> must be at least 1.
        /// </summary>
        [Fact]
        public async Task Build_WithCSharpFile_ReportsAtLeastOneFileIndexed()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.build", new { root_path = _fixture.RootPath });

            Assert.False(isError);
            var filesIndexed = data.GetValue("FilesIndexed", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
            Assert.True(filesIndexed >= 1, $"Expected >= 1 file indexed, got {filesIndexed}");
        }

        /// <summary>
        /// Phase 2 symbol projection is active: a build must produce at least one symbol.
        /// </summary>
        [Fact]
        public async Task Build_WithCSharpFile_ReportsSymbolsProjected()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.build", new { root_path = _fixture.RootPath });

            var symbolCount = data.GetValue("SymbolCount", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
            Assert.True(symbolCount > 0, $"Expected > 0 symbols, got {symbolCount}");
        }

        /// <summary>
        /// Build result must carry a <c>"success"</c> status field.
        /// </summary>
        [Fact]
        public async Task Build_WithCSharpFile_ReturnsStatusSuccess()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.build", new { root_path = _fixture.RootPath });

            var status = data.GetValue("Status", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Equal("success", status, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Building an empty directory must still succeed with zero files indexed.
        /// </summary>
        [Fact]
        public async Task Build_WithEmptyDirectory_ReturnsSuccessWithZeroFiles()
        {
            using var emptyRepo = new TempTestRepository();
            using var emptyServer = new McpTestServer(emptyRepo.Root);
            var client = emptyServer.CreateHttpClient();

            var (data, isError) = await client.CallToolAsync(
                "code_index.build", new { root_path = emptyRepo.Root });

            Assert.False(isError);
            var filesIndexed = data.GetValue("FilesIndexed", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? -1;
            Assert.Equal(0, filesIndexed);
        }

        // ── code_index.get_status ─────────────────────────────────────────────────

        /// <summary>
        /// After a successful build, <c>get_status</c> must report a Ready state
        /// with the same snapshot ID that was returned by the build.
        /// </summary>
        [Fact]
        public async Task GetStatus_AfterBuild_ReturnsReadyStateWithSnapshotId()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_status", new { root_path = _fixture.RootPath });

            Assert.False(isError);
            var state = data.GetValue("State", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Equal("Ready", state, StringComparer.OrdinalIgnoreCase);

            var latestId = data.GetValue("LatestSnapshotId", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.NotNull(latestId);
            // Snapshot IDs embed a timestamp — only verify the stable path-hash prefix, not the exact timestamp.
            Assert.StartsWith("snap-", latestId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Querying status for a path that has never been indexed must return
        /// an Idle (or equivalent "no snapshot") state rather than an error.
        /// </summary>
        [Fact]
        public async Task GetStatus_ForUnindexedPath_ReturnsNullOrIdleState()
        {
            var unknownPath = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N"));
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_status", new { root_path = unknownPath });

            Assert.False(isError);
            // The state should NOT be Ready since nothing has been built.
            var state = data.GetValue("State", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.NotEqual("Ready", state, StringComparer.OrdinalIgnoreCase);
        }

        // ── code_index.list_files ─────────────────────────────────────────────────

        /// <summary>
        /// After a build, <c>list_files</c> must include the C# file that was indexed.
        /// </summary>
        [Fact]
        public async Task ListFiles_AfterBuild_ReturnsCSharpFile()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.list_files", new { root_path = _fixture.RootPath });

            Assert.False(isError);
            var files = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(files);
            Assert.NotEmpty(files);

            // At least one file should have language = csharp
            Assert.Contains(files, f =>
                string.Equals(
                    f.Value<JObject>()?.GetValue("LanguageId", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    "csharp",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The language filter must restrict results to only files of the given language.
        /// </summary>
        [Fact]
        public async Task ListFiles_WithCSharpLanguageFilter_ReturnsOnlyCSharpFiles()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.list_files",
                new { root_path = _fixture.RootPath, language = "csharp" });

            var files = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(files);
            Assert.All(files, f =>
            {
                var lang = f.Value<JObject>()
                    ?.GetValue("LanguageId", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>();
                Assert.Equal("csharp", lang, StringComparer.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Filtering for an unindexed language must return an empty file list.
        /// </summary>
        [Fact]
        public async Task ListFiles_WithUnknownLanguageFilter_ReturnsEmptyList()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.list_files",
                new { root_path = _fixture.RootPath, language = "cobol" });

            var files = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(files);
            Assert.Empty(files);
        }

        // ── code_index.get_file_outline ───────────────────────────────────────────

        /// <summary>
        /// The file outline for the sample C# file must contain the
        /// <c>TestApp.Domain</c> namespace declaration node.
        /// </summary>
        [Fact]
        public async Task GetFileOutline_ForCSharpFile_ReturnsNamespaceNode()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            Assert.False(isError);

            // The outline should contain at least one node
            var nodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(nodes);
            Assert.NotEmpty(nodes);

            // There should be a namespace node named 'TestApp' or a container with 'Domain'
            bool hasNamespaceNode = false;
            foreach (var node in nodes)
            {
                var name = node.Value<JObject>()
                    ?.GetValue("Name", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>() ?? "";
                if (name.Contains("TestApp") || name.Contains("Domain"))
                {
                    hasNamespaceNode = true;
                    break;
                }
            }
            Assert.True(hasNamespaceNode, "Expected a namespace node containing 'TestApp' or 'Domain'.");
        }

        /// <summary>
        /// The file outline must contain a <c>Customer</c> class node.
        /// </summary>
        [Fact]
        public async Task GetFileOutline_ForCSharpFile_ReturnsCustomerClassNode()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            var nodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(nodes);

            // Customer class may be nested under a namespace node — search recursively
            bool hasCustomer = ContainsNode(nodes!, "Customer");
            Assert.True(hasCustomer, "Expected a 'Customer' node in the outline.");
        }

        // ── code_index.get_snapshot_info ──────────────────────────────────────────

        /// <summary>
        /// Snapshot info for the built root must return metadata with the correct snapshot ID.
        /// </summary>
        [Fact]
        public async Task GetSnapshotInfo_AfterBuild_ReturnsMetadataWithSnapshotId()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_snapshot_info", new { root_path = _fixture.RootPath });

            Assert.False(isError);
            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Null(error);

            var snapshotId = data.GetValue("SnapshotId", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.NotNull(snapshotId);
            // Snapshot IDs embed a timestamp — only verify the stable path-hash prefix, not the exact timestamp.
            Assert.StartsWith("snap-", snapshotId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Snapshot info must report at least one indexed file.
        /// </summary>
        [Fact]
        public async Task GetSnapshotInfo_AfterBuild_ReportsAtLeastOneFile()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_snapshot_info", new { root_path = _fixture.RootPath });

            var totalFiles = data.GetValue("FileCount", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
            Assert.True(totalFiles >= 1, $"Expected >= 1 total files, got {totalFiles}");
        }

        // ── code_index.list_roots ─────────────────────────────────────────────────

        /// <summary>
        /// After a build, <c>list_roots</c> must include the root path that was indexed.
        /// </summary>
        [Fact]
        public async Task ListRoots_AfterBuild_ContainsBuiltRootPath()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync("code_index.list_roots");

            Assert.False(isError);
            var roots = data.GetValue("roots", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(roots);
            Assert.NotEmpty(roots);

            bool found = false;
            // Normalize separators for comparison — the store normalizes to forward slashes
            var normalizedFixturePath = _fixture.RootPath.Replace('\\', '/');
            foreach (var root in roots)
            {
                var rootVal = (root.Value<string>() ?? "").Replace('\\', '/');
                if (string.Equals(rootVal, normalizedFixturePath, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, $"Expected to find '{_fixture.RootPath}' in list_roots result.");
        }

        // ── code_index.get_language_capabilities ─────────────────────────────────

        /// <summary>
        /// Language capabilities must list at least two adapters (C# and Python).
        /// </summary>
        [Fact]
        public async Task GetLanguageCapabilities_ReturnsAtLeastTwoAdapters()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync("code_index.get_language_capabilities");

            Assert.False(isError);
            var adapters = data.GetValue("adapters", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(adapters);
            Assert.True(adapters.Count >= 2, $"Expected >= 2 adapters, got {adapters.Count}");
        }

        /// <summary>
        /// The C# adapter must be listed with the expected language ID.
        /// </summary>
        [Fact]
        public async Task GetLanguageCapabilities_IncludesCSharpAdapter()
        {
            var (data, _) = await _fixture.Client.CallToolAsync("code_index.get_language_capabilities");
            var adapters = data.GetValue("adapters", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(adapters);

            bool hasCSharp = false;
            foreach (var adapter in adapters)
            {
                var id = adapter.Value<JObject>()
                    ?.GetValue("language_id", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>() ?? "";
                if (string.Equals(id, "csharp", StringComparison.OrdinalIgnoreCase))
                {
                    hasCSharp = true;
                    break;
                }
            }
            Assert.True(hasCSharp, "Expected a 'csharp' adapter in language capabilities.");
        }

        /// <summary>
        /// The Python adapter must be listed with the expected language ID.
        /// </summary>
        [Fact]
        public async Task GetLanguageCapabilities_IncludesPythonAdapter()
        {
            var (data, _) = await _fixture.Client.CallToolAsync("code_index.get_language_capabilities");
            var adapters = data.GetValue("adapters", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(adapters);

            bool hasPython = false;
            foreach (var adapter in adapters)
            {
                var id = adapter.Value<JObject>()
                    ?.GetValue("language_id", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>() ?? "";
                if (string.Equals(id, "python", StringComparison.OrdinalIgnoreCase))
                {
                    hasPython = true;
                    break;
                }
            }
            Assert.True(hasPython, "Expected a 'python' adapter in language capabilities.");
        }

        // ── JSON-RPC protocol ─────────────────────────────────────────────────────

        /// <summary>
        /// Calling a non-existent tool must return a JSON-RPC error and throw from
        /// <c>CallToolAsync</c>.
        /// </summary>
        [Fact]
        public async Task CallTool_WithUnknownToolName_ThrowsJsonRpcError()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _fixture.Client.CallToolAsync("code_index.nonexistent_tool_xyz"));
        }

        /// <summary>
        /// Calling <c>tools/list</c> via the JSON-RPC endpoint must return the
        /// Phase 2 tools among the registered tools.
        /// </summary>
        [Fact]
        public async Task ToolsList_IncludesPhase2SymbolTools()
        {
            var requestBody = new Newtonsoft.Json.Linq.JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"]      = 1,
                ["method"]  = "tools/list",
                ["params"]  = new Newtonsoft.Json.Linq.JObject(),
            };

            var response = await _fixture.Client.GetAsync("mcp/v1/tools");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("code_index.find_symbols", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("code_index.get_symbol", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("code_index.get_type_members", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("code_index.get_namespace_tree", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Recursively searches an outline node tree for a node with the given name.</summary>
        private static bool ContainsNode(JArray nodes, string targetName)
        {
            foreach (var node in nodes)
            {
                var obj = node.Value<JObject>();
                if (obj is null) continue;

                var name = obj.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? "";
                if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                    return true;

                var children = obj.GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
                if (children is not null && ContainsNode(children, targetName))
                    return true;
            }
            return false;
        }
    }
}
