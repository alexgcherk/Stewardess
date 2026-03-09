// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StewardessMCPService.IntegrationTests.Helpers;
using Xunit;

namespace StewardessMCPService.IntegrationTests.Scenarios;

[Collection(IntegrationTestCollection.Name)]
/// <summary>
/// Integration tests for the <c>/api/repo-browser</c> REST endpoints:
///
/// <list type="bullet">
///   <item>GET  /api/repo-browser/tree  — print_tree</item>
///   <item>POST /api/repo-browser/grep  — grep</item>
///   <item>GET  /api/repo-browser/file  — read_file</item>
///   <item>POST /api/repo-browser/find  — find_path</item>
/// </list>
///
/// Each test uses an in-process ASP.NET Core server backed by a temporary
/// repository that contains a minimal C# project structure.
/// </summary>
public sealed class RepoBrowserEndpointsTests : IDisposable
{
    // Relative paths for test files seeded in the temp repo
    private const string SampleFilePath = "src/Hello.cs";

    private const string SampleFileContent =
        "using System;\r\n" +
        "namespace Hello\r\n" +
        "{\r\n" +
        "    public class Greeter\r\n" +
        "    {\r\n" +
        "        public string Greet() => \"Hello, World!\";\r\n" +
        "    }\r\n" +
        "}\r\n";

    private const string SampleProjectPath = "src/Hello.csproj";

    private const string SampleProjectContent =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <TargetFramework>net8.0</TargetFramework>\r\n" +
        "  </PropertyGroup>\r\n" +
        "</Project>\r\n";

    private readonly McpRestClient _client;
    private readonly McpTestServer _server;
    private readonly TempTestRepository _tempRepo;

    public RepoBrowserEndpointsTests()
    {
        _tempRepo = new TempTestRepository();

        // Seed a small file structure so the tests have something to work with
        Directory.CreateDirectory(Path.Combine(_tempRepo.Root, "src"));
        Directory.CreateDirectory(Path.Combine(_tempRepo.Root, "docs"));
        File.WriteAllText(Path.Combine(_tempRepo.Root, SampleFilePath), SampleFileContent);
        File.WriteAllText(Path.Combine(_tempRepo.Root, SampleProjectPath), SampleProjectContent);
        File.WriteAllText(Path.Combine(_tempRepo.Root, "docs", "README.md"), "# Hello\r\nA sample project.");

        _server = new McpTestServer(_tempRepo.Root);
        _client = _server.CreateHttpClient();
    }

    public void Dispose()
    {
        _server?.Dispose();
        _tempRepo?.Dispose();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/repo-browser/tree
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>GET /api/repo-browser/tree returns 200 with a non-empty items list.</summary>
    [Fact]
    public async Task PrintTree_Get_Returns200WithItems()
    {
        var response = await _client.GetAsync("/api/repo-browser/tree");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);
    }

    /// <summary>GET /api/repo-browser/tree includes rootPath in the response.</summary>
    [Fact]
    public async Task PrintTree_Get_IncludesRootPath()
    {
        var response = await _client.GetAsync("/api/repo-browser/tree");
        var data = await ReadDataAsync(response);

        Assert.NotNull(data["rootPath"]);
        Assert.False(string.IsNullOrEmpty(data["rootPath"]?.Value<string>()));
    }

    /// <summary>Each item in the tree has path, name, kind, and depth.</summary>
    [Fact]
    public async Task PrintTree_Get_ItemsHaveRequiredFields()
    {
        var response = await _client.GetAsync("/api/repo-browser/tree");
        var data = await ReadDataAsync(response);
        var items = (JArray?)data["items"];
        Assert.NotNull(items);

        foreach (var item in items!)
        {
            Assert.NotNull(item["path"]);
            Assert.NotNull(item["name"]);
            Assert.NotNull(item["kind"]);
            Assert.NotNull(item["depth"]);
        }
    }

    /// <summary>include_files=false returns only directory items.</summary>
    [Fact]
    public async Task PrintTree_Get_IncludeFilesFalse_OnlyDirectories()
    {
        var response = await _client.GetAsync("/api/repo-browser/tree?includeFiles=false");
        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray ?? new JArray();

        foreach (var item in items)
            Assert.Equal("directory", item["kind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>max_entries=1 returns exactly one item and sets truncated=true.</summary>
    [Fact]
    public async Task PrintTree_Get_MaxEntries1_TruncatesResults()
    {
        var response = await _client.GetAsync("/api/repo-browser/tree?maxEntries=1");
        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;

        Assert.NotNull(items);
        Assert.Single(items);
        Assert.True(data["truncated"]?.Value<bool>() == true);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // POST /api/repo-browser/grep
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>POST /api/repo-browser/grep with a known query returns 200 with matches.</summary>
    [Fact]
    public async Task Grep_Post_FindsTextInSeededFile()
    {
        var response = await PostJsonAsync("/api/repo-browser/grep", new { query = "Greeter" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        // Match must point at our sample file
        var firstPath = items[0]["filePath"]?.Value<string>() ?? "";
        Assert.EndsWith("Hello.cs", firstPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>POST /api/repo-browser/grep without a query returns 400.</summary>
    [Fact]
    public async Task Grep_Post_MissingQuery_Returns400()
    {
        var response = await PostJsonAsync("/api/repo-browser/grep", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>POST /api/repo-browser/grep with null body returns 400.</summary>
    [Fact]
    public async Task Grep_Post_NullBody_Returns400()
    {
        var response = await PostJsonAsync("/api/repo-browser/grep", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>grep response contains rootPath, query, mode, matchCount fields.</summary>
    [Fact]
    public async Task Grep_Post_ResponseHasExpectedTopLevelFields()
    {
        var response = await PostJsonAsync("/api/repo-browser/grep", new { query = "namespace" });
        var data = await ReadDataAsync(response);

        Assert.NotNull(data["rootPath"]);
        Assert.Equal("namespace", data["query"]?.Value<string>());
        Assert.Equal("literal", data["mode"]?.Value<string>());
        Assert.True(data["matchCount"] != null);
    }

    /// <summary>grep match items have filePath, lineNumber, lineText.</summary>
    [Fact]
    public async Task Grep_Post_MatchItemsHaveRequiredFields()
    {
        var response = await PostJsonAsync("/api/repo-browser/grep", new { query = "class" });
        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;

        Assert.NotNull(items);
        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            Assert.NotNull(item["filePath"]);
            Assert.True(item["lineNumber"]?.Value<int>() > 0);
            Assert.NotNull(item["lineText"]);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/repo-browser/file
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>GET /api/repo-browser/file returns 200 and the file content.</summary>
    [Fact]
    public async Task ReadFile_Get_Returns200WithContent()
    {
        var response =
            await _client.GetAsync($"/api/repo-browser/file?filePath={Uri.EscapeDataString(SampleFilePath)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadDataAsync(response);
        Assert.True(data["exists"]?.Value<bool>() == true);
        Assert.False(string.IsNullOrEmpty(data["content"]?.Value<string>()));
    }

    /// <summary>GET /api/repo-browser/file without filePath returns 400.</summary>
    [Fact]
    public async Task ReadFile_Get_MissingFilePath_Returns400()
    {
        var response = await _client.GetAsync("/api/repo-browser/file");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>GET /api/repo-browser/file for a missing file returns exists=false (not 404).</summary>
    [Fact]
    public async Task ReadFile_Get_NonExistentFile_Returns200WithExistsFalse()
    {
        var response = await _client.GetAsync("/api/repo-browser/file?filePath=does/not/exist.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadDataAsync(response);
        Assert.True(data["exists"]?.Value<bool>() == false);
    }

    /// <summary>include_line_numbers=true (default) causes content to start with "1: ".</summary>
    [Fact]
    public async Task ReadFile_Get_LineNumbersEnabled_ContentStartsWith1()
    {
        var response = await _client.GetAsync(
            $"/api/repo-browser/file?filePath={Uri.EscapeDataString(SampleFilePath)}&includeLineNumbers=true");

        var data = await ReadDataAsync(response);
        var content = data["content"]?.Value<string>() ?? "";
        Assert.True(content.StartsWith("1: "), "Content should start with '1: '");
    }

    /// <summary>include_line_numbers=false returns raw file content.</summary>
    [Fact]
    public async Task ReadFile_Get_LineNumbersDisabled_ContentNoPrefix()
    {
        var response = await _client.GetAsync(
            $"/api/repo-browser/file?filePath={Uri.EscapeDataString(SampleFilePath)}&includeLineNumbers=false");

        var data = await ReadDataAsync(response);
        var content = data["content"]?.Value<string>() ?? "";
        Assert.False(content.TrimStart().StartsWith("1: "), "Content should NOT start with '1: '");
    }

    /// <summary>startLine and endLine parameters restrict the returned range.</summary>
    [Fact]
    public async Task ReadFile_Get_WithRange_ReturnsStartAndEndLine()
    {
        var response = await _client.GetAsync(
            $"/api/repo-browser/file?filePath={Uri.EscapeDataString(SampleFilePath)}&startLine=1&endLine=2");

        var data = await ReadDataAsync(response);
        Assert.True(data["exists"]?.Value<bool>() == true);
        Assert.Equal(1, data["startLine"]?.Value<int>());
        Assert.Equal(2, data["endLine"]?.Value<int>());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // POST /api/repo-browser/find
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>POST /api/repo-browser/find returns 200 and finds files by name.</summary>
    [Fact]
    public async Task FindPath_Post_FindsFileByName()
    {
        var response = await PostJsonAsync("/api/repo-browser/find", new { query = "Hello.cs" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        var firstPath = items[0]["path"]?.Value<string>() ?? "";
        Assert.EndsWith("Hello.cs", firstPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>POST /api/repo-browser/find without query returns 400.</summary>
    [Fact]
    public async Task FindPath_Post_MissingQuery_Returns400()
    {
        var response = await PostJsonAsync("/api/repo-browser/find", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>target_kind=directory finds directories only.</summary>
    [Fact]
    public async Task FindPath_Post_TargetKindDirectory_FindsDocs()
    {
        var response = await PostJsonAsync("/api/repo-browser/find",
            new { query = "docs", target_kind = "directory" });

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray ?? new JArray();

        foreach (var item in items)
            Assert.Equal("directory", item["kind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>find_path response has rootPath, query, matchMode, targetKind, resultCount.</summary>
    [Fact]
    public async Task FindPath_Post_ResponseHasExpectedTopLevelFields()
    {
        var response = await PostJsonAsync("/api/repo-browser/find", new { query = "Hello.cs" });
        var data = await ReadDataAsync(response);

        Assert.NotNull(data["rootPath"]);
        Assert.Equal("Hello.cs", data["query"]?.Value<string>());
        Assert.Equal("name", data["matchMode"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("any", data["targetKind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
        Assert.True(data["resultCount"] != null);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/repo-browser/search
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>GET /api/repo-browser/search returns 200 with items for a known filename.</summary>
    [Fact]
    public async Task Search_Get_Returns200WithItems()
    {
        var response = await _client.GetAsync("/api/repo-browser/search?query=Hello.cs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);
    }

    /// <summary>GET /api/repo-browser/search finds a file by partial name (no extension).</summary>
    [Fact]
    public async Task Search_Get_PartialName_FindsFile()
    {
        var response = await _client.GetAsync("/api/repo-browser/search?query=Hello");

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        Assert.True(
            items.Any(i =>
                (i["name"]?.Value<string>() ?? "").StartsWith("Hello", StringComparison.OrdinalIgnoreCase)),
            "Expected at least one file whose name starts with 'Hello'");
    }

    /// <summary>GET /api/repo-browser/search without query returns 400.</summary>
    [Fact]
    public async Task Search_Get_MissingQuery_Returns400()
    {
        var response = await _client.GetAsync("/api/repo-browser/search");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Search response contains expected top-level fields.</summary>
    [Fact]
    public async Task Search_Get_ResponseHasExpectedTopLevelFields()
    {
        var response = await _client.GetAsync("/api/repo-browser/search?query=Hello");
        var data = await ReadDataAsync(response);

        Assert.NotNull(data["rootPath"]);
        Assert.Equal("Hello", data["query"]?.Value<string>());
        Assert.NotNull(data["pathPrefix"]);
        Assert.NotNull(data["resultCount"]);
        Assert.NotNull(data["truncated"]);
    }

    /// <summary>Search result items always have path, name, kind="file", and sizeBytes.</summary>
    [Fact]
    public async Task Search_Get_Items_HaveRequiredFields()
    {
        var response = await _client.GetAsync("/api/repo-browser/search?query=.cs");
        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;

        Assert.NotNull(items);
        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            Assert.False(string.IsNullOrEmpty(item["path"]?.Value<string>()), "path must not be empty");
            Assert.False(string.IsNullOrEmpty(item["name"]?.Value<string>()), "name must not be empty");
            Assert.Equal("file", item["kind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
            Assert.True(item["sizeBytes"]?.Value<long>() >= 0, "sizeBytes must be non-negative");
        }
    }

    /// <summary>maxResults=1 limits results to at most one item.</summary>
    [Fact]
    public async Task Search_Get_MaxResults1_LimitsItems()
    {
        var response = await _client.GetAsync("/api/repo-browser/search?query=.cs&maxResults=1");
        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;

        Assert.NotNull(items);
        Assert.True(items.Count <= 1);
    }

    // ── regex (auto-detected) ─────────────────────────────────────────────────

    /// <summary>search auto-detects regex: suffix pattern finds only .cs files.</summary>
    [Fact]
    public async Task Search_Get_UseRegex_SuffixPattern_FindsCsFiles()
    {
        var response = await _client.GetAsync(
            $"/api/repo-browser/search?query={Uri.EscapeDataString("\\.cs$")}");

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        foreach (var item in items)
            Assert.EndsWith(".cs", item["name"]?.Value<string>() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>search auto-detects anchored regex and finds only Hello.cs, not Hello.csproj.</summary>
    [Fact]
    public async Task Search_Get_UseRegex_AnchoredPattern_ExcludesNonMatching()
    {
        var response = await _client.GetAsync(
            $"/api/repo-browser/search?query={Uri.EscapeDataString("^Hello\\.cs$")}");

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        foreach (var item in items)
            Assert.Equal("Hello.cs", item["name"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>search auto-detects regex and finds .csproj files using \\.csproj$ pattern.</summary>
    [Fact]
    public async Task Search_Get_UseRegex_CsprojPattern_FindsProjectFile()
    {
        var response = await _client.GetAsync(
            $"/api/repo-browser/search?query={Uri.EscapeDataString("\\.csproj$")}");

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        foreach (var item in items)
            Assert.EndsWith(".csproj", item["name"]?.Value<string>() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>search with a plain substring (no regex metacharacters) matches by substring, not regex.</summary>
    [Fact]
    public async Task Search_Get_PlainSubstring_AutoDetectedAsLiteral_FindsFiles()
    {
        // "Hello" has no regex metacharacters — auto-detection falls back to substring matching
        var response = await _client.GetAsync("/api/repo-browser/search?query=Hello");

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray ?? new JArray();
        Assert.NotEmpty(items);

        foreach (var item in items)
            Assert.Contains("Hello", item["name"]?.Value<string>() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // POST /api/repo-browser/find  — regex (auto-detected)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>find_path auto-detects regex and finds Hello.cs via an anchored name pattern.</summary>
    [Fact]
    public async Task FindPath_Post_UseRegex_AnchoredName_FindsOnlyHelloCs()
    {
        var response = await PostJsonAsync("/api/repo-browser/find",
            new { query = "^Hello\\.cs$", targetKind = "file" });

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        foreach (var item in items)
            Assert.Equal("Hello.cs", item["name"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>find_path auto-detects regex and path_fragment mode matches paths by regex.</summary>
    [Fact]
    public async Task FindPath_Post_UseRegex_PathFragment_MatchesByPath()
    {
        var response = await PostJsonAsync("/api/repo-browser/find",
            new { query = "src/.*\\.cs$", matchMode = "path_fragment", targetKind = "file" });

        var data = await ReadDataAsync(response);
        var items = data["items"] as JArray;
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            var path = (item["path"]?.Value<string>() ?? "").Replace('\\', '/');
            Assert.Contains("src/", path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".cs", path, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Authentication
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     All five repo_browser endpoints return 401 when an API key is required
    ///     but not provided in the request.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/repo-browser/tree")]
    [InlineData("POST", "/api/repo-browser/grep")]
    [InlineData("GET", "/api/repo-browser/file")]
    [InlineData("POST", "/api/repo-browser/find")]
    [InlineData("GET", "/api/repo-browser/search")]
    public async Task AllEndpoints_WithoutApiKey_Return401WhenRequired(string method, string path)
    {
        using var tempRepo = new TempTestRepository();
        using var authServer = new McpTestServer(tempRepo.Root, true);

        // Use the raw HttpClient which carries no Authorization header.
        var httpClient = authServer.HttpClient;

        HttpResponseMessage response;
        if (method == "GET")
            response = await httpClient.GetAsync(path);
        else
            response = await httpClient.PostAsync(
                path,
                new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Reads the response body, asserts it is a successful ApiResponse envelope,
    ///     and returns the inner <c>data</c> object.
    /// </summary>
    private static async Task<JObject> ReadDataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body), "Response body must not be empty.");

        var json = JObject.Parse(body);
        Assert.True(json["success"]?.Value<bool>() == true,
            $"Response 'success' was false. Body: {body}");

        var data = json["data"] as JObject;
        Assert.NotNull(data);
        return data;
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string path, object? body)
    {
        var json = body == null ? "null" : JsonConvert.SerializeObject(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };

        // Add the API key via a raw request since McpRestClient.PostAsync is private
        var httpClient = _server.HttpClient;
        return await httpClient.SendAsync(request);
    }
}