using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StewardessMCPService.IntegrationTests.Helpers;
using Xunit;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    /// <summary>
    /// Integration tests for Phase 5 incremental indexing: code_index.update,
    /// code_index.get_index_status, code_index.list_repositories, code_index.clear_repository.
    /// Each test gets a fresh server and temp directory via IAsyncLifetime.
    /// </summary>
    public sealed class CodeIndexIncrementalTests : IAsyncLifetime
    {
        private string _tempDir = string.Empty;
        private McpTestServer? _server;
        private McpRestClient? _client;

        public async Task InitializeAsync()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"stewardess-incr-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            WriteFile("Greeting.cs", "namespace Demo { public class Greeting { public string GetMessage() => \"Hello\"; } }");
            WriteFile("Program.cs", "using Demo; class App { static void Main() { var g = new Greeting(); } }");

            _server = new McpTestServer(repositoryRoot: _tempDir);
            _client = _server.CreateHttpClient();

            // Build initial index
            var (buildData, buildIsError) = await _client.CallToolAsync(
                "code_index.build", new { root_path = _tempDir });
            Assert.False(buildIsError);
            Assert.NotNull(Str(buildData, "SnapshotId"));
            await Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            _server?.Dispose();
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* best effort */ }
            return Task.CompletedTask;
        }

        private void WriteFile(string name, string content) =>
            File.WriteAllText(Path.Combine(_tempDir, name), content);

        private static string? Str(JObject? obj, string key) =>
            obj?.GetValue(key, StringComparison.OrdinalIgnoreCase)?.Value<string>();

        private static int Int(JObject? obj, string key, int def = 0) =>
            obj?.GetValue(key, StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? def;

        [Fact]
        public async Task GetIndexStatus_AfterBuild_ReturnsReadyState()
        {
            var (data, _) = await _client!.CallToolAsync(
                "code_index.get_index_status", new { root_path = _tempDir });

            Assert.Equal("Ready", Str(data, "state"));
            Assert.NotNull(Str(data, "latestSnapshotId"));
            Assert.True(Int(data, "fileCount") >= 2);
            Assert.True(Int(data, "symbolCount") >= 1);
        }

        [Fact]
        public async Task Update_AfterModifyingFile_ReflectsNewSymbol()
        {
            WriteFile("Greeting.cs",
                "namespace Demo {\n" +
                "    public class Greeting { public string GetMessage() => \"Hello\"; }\n" +
                "    public class FancyGreeting { public string GetMessage() => \"Hi!\"; }\n" +
                "}");

            var (data, isError) = await _client!.CallToolAsync(
                "code_index.update", new { root_path = _tempDir });

            Assert.False(isError);
            Assert.Null(Str(data, "error"));
            Assert.NotNull(Str(data, "snapshotId"));
            Assert.Equal(1, Int(data, "filesModified"));
            Assert.Equal(0, Int(data, "filesAdded"));
            Assert.Equal(0, Int(data, "filesDeleted"));

            var (findData, _) = await _client.CallToolAsync(
                "code_index.find_symbols", new { query_text = "FancyGreeting" });
            var items = findData?.GetValue("items", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray();
            Assert.True(items.Count > 0, "FancyGreeting should be findable after update");
        }

        [Fact]
        public async Task Update_AfterDeletingFile_RemovesSymbols()
        {
            File.Delete(Path.Combine(_tempDir, "Program.cs"));

            var (data, isError) = await _client!.CallToolAsync(
                "code_index.update", new { root_path = _tempDir });

            Assert.False(isError);
            Assert.Null(Str(data, "error"));
            Assert.Equal(0, Int(data, "filesModified"));
            Assert.Equal(1, Int(data, "filesDeleted"));

            var (findData, _) = await _client.CallToolAsync(
                "code_index.find_symbols", new { query_text = "App" });
            var items = findData?.GetValue("items", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray();
            Assert.Empty(items);
        }

        [Fact]
        public async Task Update_AfterAddingFile_IncludesNewSymbol()
        {
            WriteFile("Service.cs", "namespace Demo { public class EmailService { public void Send() {} } }");

            var (data, isError) = await _client!.CallToolAsync(
                "code_index.update", new { root_path = _tempDir });

            Assert.False(isError);
            Assert.Null(Str(data, "error"));
            Assert.Equal(1, Int(data, "filesAdded"));

            var (findData, _) = await _client.CallToolAsync(
                "code_index.find_symbols", new { query_text = "EmailService" });
            var items = findData?.GetValue("items", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray();
            Assert.True(items.Count > 0, "EmailService should be found after update");
        }

        [Fact]
        public async Task Update_WithNoChanges_ReturnsAllUnchanged()
        {
            var (data, isError) = await _client!.CallToolAsync(
                "code_index.update", new { root_path = _tempDir });

            Assert.False(isError);
            Assert.Null(Str(data, "error"));
            Assert.Equal(0, Int(data, "filesAdded"));
            Assert.Equal(0, Int(data, "filesModified"));
            Assert.Equal(0, Int(data, "filesDeleted"));
            Assert.True(Int(data, "filesUnchanged") >= 2);
        }

        [Fact]
        public async Task Update_FirstTime_FallsBackToFullBuild()
        {
            var freshDir = Path.Combine(Path.GetTempPath(), $"stewardess-fresh-{Guid.NewGuid():N}");
            Directory.CreateDirectory(freshDir);
            File.WriteAllText(Path.Combine(freshDir, "Hello.cs"), "class Hello {}");

            try
            {
                using var freshServer = new McpTestServer(repositoryRoot: freshDir);
                var freshClient = freshServer.CreateHttpClient();

                var (data, isError) = await freshClient.CallToolAsync(
                    "code_index.update", new { root_path = freshDir });

                Assert.False(isError);
                Assert.Null(Str(data, "error"));
                Assert.NotNull(Str(data, "snapshotId"));
            }
            finally
            {
                try { Directory.Delete(freshDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task GetIndexStatus_AfterUpdate_ReflectsDelta()
        {
            WriteFile("Extra.cs", "namespace Demo { public class Extra {} }");
            await _client!.CallToolAsync("code_index.update", new { root_path = _tempDir });

            var (data, _) = await _client.CallToolAsync(
                "code_index.get_index_status", new { root_path = _tempDir });

            Assert.Equal("Ready", Str(data, "state"));
            var delta = data?.GetValue("lastDelta", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(delta);
            var addedPaths = delta?.GetValue("addedFilePaths", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.Equal(1, addedPaths?.Count ?? 0);
        }

        [Fact]
        public async Task ListRepositories_ReturnsCurrentRepo()
        {
            var (data, _) = await _client!.CallToolAsync(
                "code_index.list_repositories", new { });

            var repos = data?.GetValue("repositories", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray();
            Assert.True(repos.Count >= 1);

            var found = repos.OfType<JObject>().Any(r =>
            {
                var rp = r.GetValue("rootPath", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? "";
                return rp.Replace('\\', '/').Equals(_tempDir.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
            });
            Assert.True(found, $"Expected to find {_tempDir} in list_repositories result");
        }

        [Fact]
        public async Task ClearRepository_RemovesIndex()
        {
            var (data, _) = await _client!.CallToolAsync(
                "code_index.clear_repository", new { root_path = _tempDir });

            Assert.Equal("cleared", Str(data, "status"));
            Assert.Equal(1, Int(data, "removedSnapshots"));

            var (statusData, _) = await _client.CallToolAsync(
                "code_index.get_index_status", new { root_path = _tempDir });
            Assert.Equal("NotIndexed", Str(statusData, "state"));
        }

        [Fact]
        public async Task ClearRepository_NonExistentRoot_ReturnsNotFound()
        {
            var (data, _) = await _client!.CallToolAsync(
                "code_index.clear_repository",
                new { root_path = @"C:\nonexistent\stewardess-path-xyz" });

            Assert.Equal("not_found", Str(data, "status"));
            Assert.Equal(0, Int(data, "removedSnapshots"));
        }

        [Fact]
        public async Task Update_PreservesUnchangedSymbols()
        {
            WriteFile("NewFile.cs", "namespace Demo { public class NewClass {} }");
            await _client!.CallToolAsync("code_index.update", new { root_path = _tempDir });

            var (findData, _) = await _client.CallToolAsync(
                "code_index.find_symbols", new { query_text = "Greeting" });

            var items = findData?.GetValue("items", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray();
            var hasGreeting = items.OfType<JObject>().Any(i =>
                i.GetValue("name", StringComparison.OrdinalIgnoreCase)?.Value<string>() == "Greeting");
            Assert.True(hasGreeting, "Greeting symbol from unchanged file should still exist after update");
        }
    }
}
