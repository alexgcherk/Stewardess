using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using StewardessMCPServive.IntegrationTests.Helpers;

namespace StewardessMCPServive.IntegrationTests.Scenarios
{
    [Collection(IntegrationTestCollection.Name)]
    /// <summary>
    /// Integration tests that verify the three Open WebUI compatibility fixes:
    ///   1. Every OpenAPI operation has an operationId (required by Open WebUI tool discovery).
    ///   2. CORS headers are present on all API responses.
    ///   3. /openapi.json redirects to /swagger/v1/swagger.json (Open WebUI default spec path).
    ///
    /// Also covers Bearer token authentication (the auth filter accepts both X-API-Key and
    /// Authorization: Bearer).
    /// </summary>
    public sealed class OpenApiIntegrationTests : IDisposable
    {
        private readonly McpTestServer     _server;
        private readonly TempTestRepository _repo;
        private readonly HttpClient        _http;

        public OpenApiIntegrationTests()
        {
            _repo   = new TempTestRepository();
            _server = new McpTestServer(_repo.Root, requireApiKey: false);
            _http   = _server.HttpClient;
        }

        public void Dispose()
        {
            _server?.Dispose();
            _repo?.Dispose();
        }

        // ── 1. operationId ────────────────────────────────────────────────────────

        [Fact]
        public async Task SwaggerSpec_AllOperations_HaveOperationId()
        {
            var response = await _http.GetAsync("/swagger/v1/swagger.json");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var spec  = JObject.Parse(body);

            var missing = (
                from path  in spec["paths"]!.Children<JProperty>()
                from method in path.Value.Children<JProperty>()
                where string.IsNullOrWhiteSpace(method.Value["operationId"]?.Value<string>())
                select $"{method.Name.ToUpper()} {path.Name}"
            ).ToList();

            Assert.True(missing.Count == 0,
                $"The following operations are missing an operationId:\n  {string.Join("\n  ", missing)}");
        }

        [Fact]
        public async Task SwaggerSpec_OperationIds_FollowControllerActionPattern()
        {
            var response = await _http.GetAsync("/swagger/v1/swagger.json");
            var body = await response.Content.ReadAsStringAsync();
            var spec  = JObject.Parse(body);

            var malformed = (
                from path   in spec["paths"]!.Children<JProperty>()
                from method in path.Value.Children<JProperty>()
                let opId = method.Value["operationId"]?.Value<string>() ?? ""
                where !opId.Contains('_')
                select $"{method.Name.ToUpper()} {path.Name} → '{opId}'"
            ).ToList();

            Assert.True(malformed.Count == 0,
                $"OperationIds must follow 'Controller_Action' format. Malformed:\n  {string.Join("\n  ", malformed)}");
        }

        [Fact]
        public async Task SwaggerSpec_ContainsExpectedOperationIds()
        {
            var response = await _http.GetAsync("/swagger/v1/swagger.json");
            var body = await response.Content.ReadAsStringAsync();
            var spec  = JObject.Parse(body);

            var allIds = (
                from path   in spec["paths"]!.Children<JProperty>()
                from method in path.Value.Children<JProperty>()
                select method.Value["operationId"]?.Value<string>()
            ).ToHashSet(StringComparer.Ordinal);

            // Sample a representative set of expected operation IDs.
            string[] expected =
            [
                "Health_GetHealth",
                "Capabilities_GetCapabilities",
                "Capabilities_GetTools",
                "File_ReadFile",
                "Repository_ListTree",
                "Search_SearchTextGet",
                "Edit_PatchFile",
                "Git_GetStatus",
                "Command_RunBuild",
            ];

            foreach (var id in expected)
                Assert.True(allIds.Contains(id), $"Expected operationId '{id}' not found in spec.");
        }

        // ── 2. CORS ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Cors_GetRequest_IncludesAllowOriginHeader()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
            request.Headers.Add("Origin", "http://localhost:3000");

            var response = await _http.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out var vals),
                "Expected Access-Control-Allow-Origin header to be present on GET response.");
            Assert.Equal("*", vals.First());
        }

        [Fact]
        public async Task Cors_PreflightRequest_ReturnsAllowOriginHeader()
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/health");
            request.Headers.Add("Origin", "http://open-webui.local");
            request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "Authorization, Content-Type");

            var response = await _http.SendAsync(request);

            // OPTIONS preflight returns 200 or 204 with CORS headers.
            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.NoContent,
                $"Expected 200/204 for preflight, got {(int)response.StatusCode}.");

            Assert.True(
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out var vals),
                "Expected Access-Control-Allow-Origin header on preflight response.");
            Assert.Equal("*", vals.First());
        }

        [Theory]
        [InlineData("/api/health")]
        [InlineData("/api/capabilities")]
        [InlineData("/swagger/v1/swagger.json")]
        public async Task Cors_MultipleEndpoints_AllReturnAllowOriginHeader(string path)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Origin", "http://my-open-webui.example.com");

            var response = await _http.SendAsync(request);

            Assert.True(
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out _),
                $"Expected Access-Control-Allow-Origin header on GET {path}.");
        }

        // ── 3. /openapi.json alias ────────────────────────────────────────────────


        [Fact]
        public async Task OpenApiJsonAlias_AfterFollowingRedirect_ReturnsValidSpec()
        {
            // Default HttpClient follows the redirect automatically.
            var response = await _http.GetAsync("/openapi.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var spec  = JObject.Parse(body);

            // Must be a valid OpenAPI 3.x document.
            Assert.NotNull(spec["openapi"]);
            Assert.NotNull(spec["info"]);
            Assert.NotNull(spec["paths"]);

            Assert.StartsWith("3.", spec["openapi"]!.Value<string>(),
                StringComparison.Ordinal);
        }

        // ── 4. Bearer token authentication ────────────────────────────────────────

        [Fact]
        public async Task Auth_BearerToken_IsAcceptedAsApiKey()
        {
            using var repo   = new TempTestRepository();
            using var server = new McpTestServer(repo.Root, requireApiKey: true);

            var apiKey = server.Settings.ApiKey;
            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/detailed");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Auth_XApiKeyHeader_IsAcceptedAsApiKey()
        {
            using var repo   = new TempTestRepository();
            using var server = new McpTestServer(repo.Root, requireApiKey: true);

            var apiKey = server.Settings.ApiKey;
            var client = server.HttpClient;

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/detailed");
            request.Headers.Add("X-API-Key", apiKey);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Auth_NoKey_Returns401_WhenApiKeyRequired()
        {
            using var repo   = new TempTestRepository();
            using var server = new McpTestServer(repo.Root, requireApiKey: true);

            var response = await server.HttpClient.GetAsync("/api/health/detailed");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Auth_WrongBearerToken_Returns401()
        {
            using var repo   = new TempTestRepository();
            using var server = new McpTestServer(repo.Root, requireApiKey: true);

            var client  = server.HttpClient;
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/detailed");
            request.Headers.Add("Authorization", "Bearer wrong-key");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Auth_BearerAndXApiKeyBothPresent_XApiKeyTakesPrecedence()
        {
            using var repo   = new TempTestRepository();
            using var server = new McpTestServer(repo.Root, requireApiKey: true);

            var apiKey = server.Settings.ApiKey;
            var client  = server.HttpClient;

            // X-API-Key is correct; Bearer is wrong — X-API-Key wins (checked first).
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/detailed");
            request.Headers.Add("X-API-Key", apiKey);
            request.Headers.Add("Authorization", "Bearer wrong-key");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
