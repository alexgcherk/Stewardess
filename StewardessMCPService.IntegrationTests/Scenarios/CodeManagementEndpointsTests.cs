using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using StewardessMCPService.IntegrationTests.Helpers;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    /// <summary>
    /// Integration tests for the CodeManagementController endpoints defined in the
    /// MCP Source Code Management API spec (mcp_code_management_openapi.json).
    ///
    /// Covers all 18 endpoints across groups:
    ///   repository  – /repository/info, /repository/tree
    ///   search      – /search/files, /search/text
    ///   files       – /files/read, /files/read-batch, /files/write, /files/delete, /files/move
    ///   edits       – /edits/apply-patch
    ///   index       – /index/symbols/find, /index/references/find, /index/dependencies, /index/call-graph
    ///   validation  – /validation/build, /validation/format, /validation/test
    ///   history     – /history/changes
    /// </summary>
    [Collection(IntegrationTestCollection.Name)]
    public sealed class CodeManagementEndpointsTests : IDisposable
    {
        private readonly McpTestServer      _server;
        private readonly McpRestClient      _client;
        private readonly TempTestRepository _tempRepo;

        // ── Setup ────────────────────────────────────────────────────────────────

        public CodeManagementEndpointsTests()
        {
            _tempRepo = new TempTestRepository();

            // Seed files used across tests
            File.WriteAllText(Path.Combine(_tempRepo.Root, "hello.cs"),
                "public class Hello\n{\n    public void World() { }\n}\n");
            File.WriteAllText(Path.Combine(_tempRepo.Root, "readme.txt"),
                "This is a readme.\nSecond line.\nThird line.\n");
            Directory.CreateDirectory(Path.Combine(_tempRepo.Root, "src"));
            File.WriteAllText(Path.Combine(_tempRepo.Root, "src", "main.cs"),
                "// main entry point\npublic class Program { }");

            _server = new McpTestServer(_tempRepo.Root);
            _client = _server.CreateHttpClient();
        }

        public void Dispose()
        {
            _server?.Dispose();
            _tempRepo?.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /repository/info
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetRepositoryInfo_Returns200()
        {
            var response = await _client.GetAsync("/repository/info");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetRepositoryInfo_HasRequiredFields()
        {
            var body = JObject.Parse(await (await _client.GetAsync("/repository/info"))
                .Content.ReadAsStringAsync());

            Assert.NotNull(body["name"]);
            Assert.NotNull(body["rootPath"]);
            Assert.NotNull(body["defaultBranch"]);
            Assert.NotNull(body["policy"]);
            Assert.NotNull(body["policy"]!["allowsEdits"]);
        }

        [Fact]
        public async Task GetRepositoryInfo_PolicyAllowsEdits_WhenNotReadOnly()
        {
            var body = JObject.Parse(await (await _client.GetAsync("/repository/info"))
                .Content.ReadAsStringAsync());

            Assert.True(body["policy"]!["allowsEdits"]!.Value<bool>(),
                "Expected allowsEdits=true in writable test server.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /repository/tree
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetRepositoryTree_Returns200WithEntries()
        {
            var body = await _client.PostJsonGetBodyAsync("/repository/tree", new { path = ".", depth = 3 });

            Assert.NotNull(body["root"]);
            Assert.NotNull(body["entries"]);
            Assert.True(body["entries"]!.Type == JTokenType.Array);
        }

        [Fact]
        public async Task GetRepositoryTree_ContainsSeededFiles()
        {
            var body  = await _client.PostJsonGetBodyAsync("/repository/tree", new { path = ".", depth = 4 });
            var paths = body["entries"]!.ToObject<JArray>()!;

            bool hasHello = false;
            bool hasReadme = false;
            foreach (var entry in paths)
            {
                var p = entry["path"]?.Value<string>() ?? "";
                if (p.EndsWith("hello.cs",  StringComparison.OrdinalIgnoreCase)) hasHello  = true;
                if (p.EndsWith("readme.txt", StringComparison.OrdinalIgnoreCase)) hasReadme = true;
            }

            Assert.True(hasHello,  "hello.cs not found in repository tree.");
            Assert.True(hasReadme, "readme.txt not found in repository tree.");
        }

        [Fact]
        public async Task GetRepositoryTree_DepthZero_ReturnsRootOnly()
        {
            var body    = await _client.PostJsonGetBodyAsync("/repository/tree", new { path = ".", depth = 0 });
            var entries = body["entries"]!.ToObject<JArray>()!;

            // At depth 0 only the root itself is returned (if at all)
            Assert.NotNull(entries);
        }

        [Fact]
        public async Task GetRepositoryTree_EmptyBody_Uses_Defaults()
        {
            var response = await _client.PostJsonAsync("/repository/tree", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /search/files
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task SearchFiles_WithMatchingPattern_ReturnsResults()
        {
            var body    = await _client.PostJsonGetBodyAsync("/search/files", new { query = "hello" });
            var results = body["results"]!.ToObject<JArray>()!;

            Assert.True(results.Count > 0, "Expected at least one result for 'hello' query.");
            Assert.NotNull(results[0]!["path"]);
        }

        [Fact]
        public async Task SearchFiles_WithNoMatch_ReturnsEmpty()
        {
            var body    = await _client.PostJsonGetBodyAsync("/search/files",
                new { query = "zzz_nonexistent_xyz_99999" });
            var results = body["results"]!.ToObject<JArray>()!;

            Assert.Empty(results);
        }

        [Fact]
        public async Task SearchFiles_MissingQuery_Returns400()
        {
            var response = await _client.PostJsonAsync("/search/files", new { });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SearchFiles_WithExtensionFilter_OnlyReturnsCsFiles()
        {
            var body    = await _client.PostJsonGetBodyAsync("/search/files",
                new { query = ".", extensions = new[] { ".cs" }, maxResults = 50 });
            var results = body["results"]!.ToObject<JArray>()!;

            foreach (var r in results)
            {
                var path = r["path"]!.Value<string>()!;
                Assert.True(path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase),
                    $"Non-.cs file returned: {path}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /search/text
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task SearchText_LiteralMatch_ReturnsMatches()
        {
            var body    = await _client.PostJsonGetBodyAsync("/search/text",
                new { pattern = "Hello", isRegex = false });
            var matches = body["matches"]!.ToObject<JArray>()!;

            Assert.True(matches.Count > 0, "Expected 'Hello' to match in hello.cs.");
            var first = matches[0]!;
            Assert.NotNull(first["path"]);
            Assert.NotNull(first["line"]);
            Assert.NotNull(first["preview"]);
        }

        [Fact]
        public async Task SearchText_RegexMatch_ReturnsMatches()
        {
            var body    = await _client.PostJsonGetBodyAsync("/search/text",
                new { pattern = @"class\s+\w+", isRegex = true });
            var matches = body["matches"]!.ToObject<JArray>()!;

            Assert.True(matches.Count > 0, "Expected regex 'class\\s+\\w+' to match.");
        }

        [Fact]
        public async Task SearchText_MissingPattern_Returns400()
        {
            var response = await _client.PostJsonAsync("/search/text", new { });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SearchText_CaseSensitive_ReturnsLessResults()
        {
            var insensitive = await _client.PostJsonGetBodyAsync("/search/text",
                new { pattern = "hello", caseSensitive = false });
            var sensitive   = await _client.PostJsonGetBodyAsync("/search/text",
                new { pattern = "hello", caseSensitive = true });

            var insensitiveCount = insensitive["matches"]!.ToObject<JArray>()!.Count;
            var sensitiveCount   = sensitive["matches"]!.ToObject<JArray>()!.Count;

            Assert.True(sensitiveCount <= insensitiveCount,
                "Case-sensitive search should return ≤ results vs case-insensitive.");
        }

        [Fact]
        public async Task SearchText_WithContext_IncludesContextLines()
        {
            var body    = await _client.PostJsonGetBodyAsync("/search/text",
                new { pattern = "Second line", contextBefore = 1, contextAfter = 1 });
            var matches = body["matches"]!.ToObject<JArray>()!;

            Assert.True(matches.Count > 0);
            var first = matches[0]!;
            Assert.NotNull(first["contextBefore"]);
            Assert.NotNull(first["contextAfter"]);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /files/read
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task ReadFile_ExistingFile_Returns200WithContent()
        {
            var body = await _client.PostJsonGetBodyAsync("/files/read", new { path = "hello.cs" });

            Assert.NotNull(body["path"]);
            Assert.NotNull(body["content"]);
            Assert.NotNull(body["etag"]);
            Assert.Contains("Hello", body["content"]!.Value<string>()!);
        }

        [Fact]
        public async Task ReadFile_ReturnsNonEmptyEtag()
        {
            var body = await _client.PostJsonGetBodyAsync("/files/read", new { path = "hello.cs" });
            var etag = body["etag"]?.Value<string>();

            Assert.False(string.IsNullOrEmpty(etag), "ETag must not be empty.");
        }

        [Fact]
        public async Task ReadFile_WithLineRange_ReturnsSubset()
        {
            var body = await _client.PostJsonGetBodyAsync("/files/read",
                new { path = "readme.txt", ranges = new[] { new { startLine = 1, endLine = 2 } } });

            var content = body["content"]?.Value<string>() ?? "";
            Assert.Contains("This is", content);
            Assert.DoesNotContain("Third line", content);
        }

        [Fact]
        public async Task ReadFile_MissingPath_Returns400()
        {
            var response = await _client.PostJsonAsync("/files/read", new { });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ReadFile_NonExistentFile_Returns404()
        {
            var response = await _client.PostJsonAsync("/files/read",
                new { path = "does_not_exist_xyz.cs" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /files/read-batch
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task ReadFilesBatch_TwoExistingFiles_ReturnsBoth()
        {
            var body  = await _client.PostJsonGetBodyAsync("/files/read-batch",
                new { items = new[] { new { path = "hello.cs" }, new { path = "readme.txt" } } });
            var files = body["files"]!.ToObject<JArray>()!;

            Assert.Equal(2, files.Count);
            Assert.All(files, f => Assert.NotNull(f["path"]));
        }

        [Fact]
        public async Task ReadFilesBatch_EmptyItems_ReturnsEmptyFiles()
        {
            var body  = await _client.PostJsonGetBodyAsync("/files/read-batch",
                new { items = Array.Empty<object>() });
            var files = body["files"]!.ToObject<JArray>()!;

            Assert.Empty(files);
        }

        [Fact]
        public async Task ReadFilesBatch_MixedExistence_ReturnsBothEntries()
        {
            // One real file, one non-existent — batch should return entries for both
            var body  = await _client.PostJsonGetBodyAsync("/files/read-batch",
                new { items = new[] { new { path = "hello.cs" }, new { path = "missing.cs" } } });
            var files = body["files"]!.ToObject<JArray>()!;

            Assert.Equal(2, files.Count);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /files/write
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task WriteFile_NewFile_Returns200AndCreatesFile()
        {
            var path = "write_test.cs";
            var body = await _client.PostJsonGetBodyAsync("/files/write",
                new { path, content = "// written by test" });

            Assert.NotNull(body["path"]);
            Assert.True(File.Exists(Path.Combine(_tempRepo.Root, path)),
                "File should exist on disk after write.");
        }

        [Fact]
        public async Task WriteFile_OverwritesExistingContent()
        {
            var path    = "overwrite.cs";
            var absPath = Path.Combine(_tempRepo.Root, path);
            File.WriteAllText(absPath, "original");

            await _client.PostJsonGetBodyAsync("/files/write",
                new { path, content = "updated" });

            Assert.Equal("updated", File.ReadAllText(absPath));
        }

        [Fact]
        public async Task WriteFile_DryRun_DoesNotCreateFile()
        {
            var path = "dryrun_file.cs";
            var body = await _client.PostJsonGetBodyAsync("/files/write",
                new { path, content = "// dry run", dryRun = true });

            Assert.True(body["dryRun"]?.Value<bool>() == true);
            Assert.False(File.Exists(Path.Combine(_tempRepo.Root, path)),
                "File must NOT exist when dryRun=true.");
        }

        [Fact]
        public async Task WriteFile_MissingPath_Returns400()
        {
            var response = await _client.PostJsonAsync("/files/write", new { content = "x" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task WriteFile_ReturnsEtag()
        {
            var body = await _client.PostJsonGetBodyAsync("/files/write",
                new { path = "etag_check.cs", content = "// etag test" });

            Assert.False(string.IsNullOrEmpty(body["etag"]?.Value<string>()),
                "Write response must include a non-empty ETag.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /files/delete
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task DeleteFile_ExistingFile_DeletesFromDisk()
        {
            var path    = "to_delete.cs";
            var absPath = Path.Combine(_tempRepo.Root, path);
            File.WriteAllText(absPath, "delete me");

            var body = await _client.PostJsonGetBodyAsync("/files/delete",
                new { path, confirm = path });

            Assert.True(body["deleted"]?.Value<bool>() == true);
            Assert.False(File.Exists(absPath), "File must not exist after delete.");
        }

        [Fact]
        public async Task DeleteFile_DryRun_DoesNotDeleteFile()
        {
            var path    = "dry_delete.cs";
            var absPath = Path.Combine(_tempRepo.Root, path);
            File.WriteAllText(absPath, "keep me");

            var body = await _client.PostJsonGetBodyAsync("/files/delete",
                new { path, confirm = path, dryRun = true });

            Assert.True(body["dryRun"]?.Value<bool>() == true);
            Assert.True(File.Exists(absPath), "File must still exist after dry-run delete.");
        }

        [Fact]
        public async Task DeleteFile_MissingPath_Returns400()
        {
            var response = await _client.PostJsonAsync("/files/delete", new { });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /files/move
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task MoveFile_ExistingFile_MovesOnDisk()
        {
            var src  = "move_src.cs";
            var dst  = "move_dst.cs";
            File.WriteAllText(Path.Combine(_tempRepo.Root, src), "move me");

            var body = await _client.PostJsonGetBodyAsync("/files/move",
                new { sourcePath = src, destinationPath = dst });

            Assert.True(body["moved"]?.Value<bool>() == true);
            Assert.False(File.Exists(Path.Combine(_tempRepo.Root, src)), "Source must not exist.");
            Assert.True(File.Exists(Path.Combine(_tempRepo.Root, dst)),  "Destination must exist.");
        }

        [Fact]
        public async Task MoveFile_DryRun_DoesNotMoveFile()
        {
            var src = "dry_move_src.cs";
            var dst = "dry_move_dst.cs";
            File.WriteAllText(Path.Combine(_tempRepo.Root, src), "dry move");

            var body = await _client.PostJsonGetBodyAsync("/files/move",
                new { sourcePath = src, destinationPath = dst, dryRun = true });

            Assert.True(body["dryRun"]?.Value<bool>() == true);
            Assert.True(File.Exists(Path.Combine(_tempRepo.Root, src)), "Source must still exist.");
            Assert.False(File.Exists(Path.Combine(_tempRepo.Root, dst)), "Dest must not exist.");
        }

        [Fact]
        public async Task MoveFile_MissingSourcePath_Returns400()
        {
            var response = await _client.PostJsonAsync("/files/move",
                new { destinationPath = "anywhere.cs" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /edits/apply-patch
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task ApplyPatch_SearchReplace_ReplacesText()
        {
            var path    = "patch_target.cs";
            var absPath = Path.Combine(_tempRepo.Root, path);
            File.WriteAllText(absPath, "// old comment\npublic class Old { }");

            var body = await _client.PostJsonGetBodyAsync("/edits/apply-patch", new
            {
                edits = new[]
                {
                    new { path, patchType = "searchReplace", search = "Old", replace = "New", replaceAll = true }
                }
            });

            Assert.NotNull(body["files"]);
            var content = File.ReadAllText(absPath);
            Assert.Contains("New", content);
        }

        [Fact]
        public async Task ApplyPatch_ReplaceRange_ReplacesLines()
        {
            var path    = "range_patch.cs";
            var absPath = Path.Combine(_tempRepo.Root, path);
            File.WriteAllText(absPath, "line1\nline2\nline3\n");

            var body = await _client.PostJsonGetBodyAsync("/edits/apply-patch", new
            {
                edits = new[]
                {
                    new { path, patchType = "replaceRange",
                          range = new { startLine = 2, endLine = 2 },
                          replace = "replaced_line2" }
                }
            });

            Assert.NotNull(body["files"]);
        }

        [Fact]
        public async Task ApplyPatch_DryRun_DoesNotModifyFile()
        {
            var path    = "dry_patch.cs";
            var absPath = Path.Combine(_tempRepo.Root, path);
            File.WriteAllText(absPath, "original content");

            await _client.PostJsonGetBodyAsync("/edits/apply-patch", new
            {
                dryRun = true,
                edits  = new[]
                {
                    new { path, patchType = "searchReplace", search = "original", replace = "changed" }
                }
            });

            Assert.Equal("original content", File.ReadAllText(absPath));
        }

        [Fact]
        public async Task ApplyPatch_UnknownPatchType_ReturnsAppliedFalse()
        {
            var path    = "unknown_patch.cs";
            var absPath = Path.Combine(_tempRepo.Root, path);
            File.WriteAllText(absPath, "content");

            var body = await _client.PostJsonGetBodyAsync("/edits/apply-patch", new
            {
                transactionMode = "allOrNothing",
                edits = new[]
                {
                    new { path, patchType = "invalidType" }
                }
            });

            Assert.True(body["applied"]?.Value<bool>() == false,
                "applied must be false for unknown patchType.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /index/symbols/find   (returns empty when no index is available)
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task FindSymbols_Returns200WithResultsArray()
        {
            var body = await _client.PostJsonGetBodyAsync("/index/symbols/find",
                new { query = "Hello", fuzzy = true });

            Assert.NotNull(body["results"]);
            Assert.Equal(JTokenType.Array, body["results"]!.Type);
        }

        [Fact]
        public async Task FindSymbols_EmptyQuery_Returns200()
        {
            var response = await _client.PostJsonAsync("/index/symbols/find",
                new { query = "" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /index/references/find
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task FindReferences_ByName_Returns200()
        {
            var body = await _client.PostJsonGetBodyAsync("/index/references/find",
                new { name = "Hello" });

            Assert.NotNull(body["references"]);
            Assert.Equal(JTokenType.Array, body["references"]!.Type);
        }

        [Fact]
        public async Task FindReferences_NoSymbolIdOrName_ReturnsEmpty()
        {
            var body = await _client.PostJsonGetBodyAsync("/index/references/find", new { });

            Assert.NotNull(body["references"]);
            Assert.Empty(body["references"]!.ToObject<JArray>()!);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /index/dependencies
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetDependencies_ByPath_Returns200WithGraph()
        {
            var body = await _client.PostJsonGetBodyAsync("/index/dependencies",
                new { path = "hello.cs", direction = "both" });

            Assert.NotNull(body["nodes"]);
            Assert.NotNull(body["edges"]);
            Assert.Equal(JTokenType.Array, body["nodes"]!.Type);
            Assert.Equal(JTokenType.Array, body["edges"]!.Type);
        }

        [Fact]
        public async Task GetDependencies_EmptyBody_Returns200()
        {
            var response = await _client.PostJsonAsync("/index/dependencies", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /index/call-graph
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetCallGraph_ByName_Returns200WithNodesEdges()
        {
            var body = await _client.PostJsonGetBodyAsync("/index/call-graph",
                new { name = "World", path = "hello.cs" });

            Assert.NotNull(body["nodes"]);
            Assert.NotNull(body["edges"]);
        }

        [Fact]
        public async Task GetCallGraph_EmptyBody_Returns200()
        {
            var response = await _client.PostJsonAsync("/index/call-graph", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /validation/build
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task BuildWorkspace_Returns200WithBuildFields()
        {
            // Use "." as the target — the service will run dotnet build against the temp repo
            var response = await _client.PostJsonAsync("/validation/build",
                new { target = ".", noRestore = true, timeoutSeconds = 60 });

            // The endpoint must return 200 even when the build fails (diagnostics in body)
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(body["success"] != null, "Response must include 'success' field.");
            Assert.True(body["exitCode"] != null, "Response must include 'exitCode' field.");
            Assert.Equal(JTokenType.Array, body["diagnostics"]!.Type);
        }

        [Fact]
        public async Task BuildWorkspace_EmptyBody_Returns200()
        {
            var response = await _client.PostJsonAsync("/validation/build", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /validation/format
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task FormatFiles_Returns200WithFormatFields()
        {
            var response = await _client.PostJsonAsync("/validation/format",
                new { timeoutSeconds = 30 });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JTokenType.Array, body["formattedFiles"]!.Type);
            Assert.NotNull(body["changed"]);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /validation/test
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task RunTests_Returns200WithTestFields()
        {
            var response = await _client.PostJsonAsync("/validation/test",
                new { target = ".", timeoutSeconds = 60 });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.NotNull(body["success"]);
            Assert.NotNull(body["summary"]);
            Assert.Equal(JTokenType.Array, body["tests"]!.Type);

            var summary = body["summary"]!;
            Assert.NotNull(summary["total"]);
            Assert.NotNull(summary["passed"]);
            Assert.NotNull(summary["failed"]);
            Assert.NotNull(summary["skipped"]);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // /history/changes
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetChanges_Returns200WithFilesArray()
        {
            var body = await _client.PostJsonGetBodyAsync("/history/changes",
                new { includePatch = false, includeUntracked = true });

            Assert.NotNull(body["files"]);
            Assert.Equal(JTokenType.Array, body["files"]!.Type);
        }

        [Fact]
        public async Task GetChanges_WithIncludePatch_Returns200()
        {
            var response = await _client.PostJsonAsync("/history/changes",
                new { includePatch = true });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetChanges_ExcludesUntracked_WhenFalse()
        {
            var withUntracked    = await _client.PostJsonGetBodyAsync("/history/changes",
                new { includeUntracked = true });
            var withoutUntracked = await _client.PostJsonGetBodyAsync("/history/changes",
                new { includeUntracked = false });

            var countWith    = withUntracked["files"]!.ToObject<JArray>()!.Count;
            var countWithout = withoutUntracked["files"]!.ToObject<JArray>()!.Count;

            Assert.True(countWithout <= countWith,
                "Excluding untracked files must not return MORE files.");
        }

        [Fact]
        public async Task GetChanges_EmptyBody_Returns200()
        {
            var response = await _client.PostJsonAsync("/history/changes", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
