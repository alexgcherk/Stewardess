using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using StewardessMCPService.Configuration;
using StewardessMCPService.IntegrationTests.Helpers;
using StewardessMCPService.Models;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    [Collection(IntegrationTestCollection.Name)]
    /// <summary>
    /// Integration tests for discovery endpoints.
    /// Tests: /api/health, /api/capabilities, /api/tools, /api/repository/info
    /// </summary>
    public class DiscoveryEndpointsTests : IDisposable
    {
        private readonly McpTestServer _server;
        private readonly McpRestClient _client;
        private readonly TempTestRepository _tempRepo;

        public DiscoveryEndpointsTests()
        {
            _tempRepo = new TempTestRepository();
            _server = new McpTestServer(_tempRepo.Root);
            _client = _server.CreateHttpClient();
        }

        public void Dispose()
        {
            _server?.Dispose();
            _tempRepo?.Dispose();
        }

        /// <summary>
        /// GET /api/health should return 200 OK with basic health status.
        /// No authentication required.
        /// </summary>
        [Fact]
        public async Task GetHealth_ReturnsOkWithValidResponse()
        {
            var response = await _client.GetAsync("/api/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            // Verify ApiResponse envelope
            Assert.True(json["success"]?.Value<bool>() == true);
            Assert.NotNull(json["data"]);
            Assert.Null(json["error"]);

            // Verify health data
            var data = json["data"];
            Assert.Equal("healthy", data["status"]?.Value<string>());
            Assert.NotNull(data["serviceVersion"]);
            Assert.NotNull(data["timestamp"]);
        }

        /// <summary>
        /// GET /api/health/detailed should return extended health info including config.
        /// </summary>
        [Fact]
        public async Task GetDetailedHealth_ReturnsOkWithConfigSummary()
        {
            var response = await _client.GetAsync("/api/health/detailed");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            Assert.True(json["success"]?.Value<bool>() == true);

            var data = json["data"];
            Assert.NotNull(data["status"]);
            Assert.NotNull(data["repositoryAccessible"]);
            Assert.NotNull(data["readOnlyMode"]);
            Assert.NotNull(data["apiKeyRequired"]);
            Assert.NotNull(data["ipAllowlistActive"]);
            Assert.NotNull(data["registeredServices"]);

            // Verify registered services contains expected entries
            var services = data["registeredServices"] as JArray;
            Assert.NotNull(services);
            Assert.NotEmpty(services);
        }

        /// <summary>
        /// GET /api/version should return just the version string.
        /// </summary>
        [Fact]
        public async Task GetVersion_ReturnsOkWithVersionString()
        {
            var response = await _client.GetAsync("/api/version");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            Assert.True(json["success"]?.Value<bool>() == true);
            Assert.NotNull(json["data"]["version"]);
            Assert.NotEmpty(json["data"]["version"].Value<string>());
        }

        /// <summary>
        /// GET /api/capabilities should return the full capability manifest.
        /// Must include tools, capabilities, constraints, and repository context.
        /// </summary>
        [Fact]
        public async Task GetCapabilities_ReturnsFullManifestWithAllSections()
        {
            var response = await _client.GetAsync("/api/capabilities");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            Assert.True(json["success"]?.Value<bool>() == true);

            var manifest = json["data"];
            Assert.NotNull(manifest["serviceVersion"]);
            Assert.NotNull(manifest["generatedAt"]);
            Assert.NotNull(manifest["capabilities"]);
            Assert.NotNull(manifest["constraints"]);
            Assert.NotNull(manifest["repositoryContext"]);
            Assert.NotNull(manifest["tools"]);

            // Verify capabilities structure
            var caps = manifest["capabilities"];
            Assert.NotNull(caps["canRead"]);
            Assert.NotNull(caps["canWrite"]);
            Assert.NotNull(caps["canSearch"]);
            Assert.NotNull(caps["canExecuteCommands"]);
            Assert.NotNull(caps["canAccessGit"]);

            // Verify constraints structure
            var constraints = manifest["constraints"];
            Assert.NotNull(constraints["maxFileReadBytes"]);
            Assert.NotNull(constraints["maxSearchResults"]);
            Assert.NotNull(constraints["blockedFolders"]);
            Assert.NotNull(constraints["blockedExtensions"]);

            // Verify repository context exists (root comes from McpServiceSettings.Instance / web.config in tests)
            var repoCtx = manifest["repositoryContext"];
            Assert.NotNull(repoCtx["repositoryName"]);
            Assert.NotNull(repoCtx["repositoryRoot"]);

            // Verify tools list is not empty
            var tools = manifest["tools"] as JArray;
            Assert.NotNull(tools);
            Assert.NotEmpty(tools);
        }

        /// <summary>
        /// GET /api/tools should return only the tools array (without manifest wrapper).
        /// </summary>
        [Fact]
        public async Task GetTools_ReturnsToolsArrayOnly()
        {
            var response = await _client.GetAsync("/api/tools");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            Assert.True(json["success"]?.Value<bool>() == true);

            var tools = json["data"] as JArray;
            Assert.NotNull(tools);
            Assert.NotEmpty(tools);

            // Verify each tool has required schema
            foreach (var tool in tools)
            {
                Assert.NotNull(tool["name"]);
                Assert.NotNull(tool["description"]);
                Assert.NotNull(tool["inputSchema"]);
            }
        }

        /// <summary>
        /// GET /repositories/default should return repository metadata.
        /// </summary>
        [Fact]
        public async Task GetRepositoryInfo_ReturnsRepositoryMetadata()
        {
            var response = await _client.GetAsync("/repositories/default");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            // New spec: direct object, no ApiResponse envelope
            Assert.NotNull(json["id"]);
            Assert.NotNull(json["name"]);
            Assert.NotNull(json["owner"]);
        }

        /// <summary>
        /// GET /api/capabilities and /api/tools should describe the same set of tools.
        /// </summary>
        [Fact]
        public async Task CapabilitiesAndTools_ShouldDescribeSameTools()
        {
            var capResponse = await _client.GetAsync("/api/capabilities");
            var toolResponse = await _client.GetAsync("/api/tools");

            var capBody = await capResponse.Content.ReadAsStringAsync();
            var toolBody = await toolResponse.Content.ReadAsStringAsync();

            var capJson = JObject.Parse(capBody);
            var toolJson = JObject.Parse(toolBody);

            var capTools = capJson["data"]["tools"] as JArray;
            var toolsArray = toolJson["data"] as JArray;

            Assert.NotNull(capTools);
            Assert.NotNull(toolsArray);
            Assert.Equal(capTools.Count, toolsArray.Count);

            // Verify tool names match
            for (int i = 0; i < capTools.Count; i++)
            {
                var capToolName = capTools[i]["name"]?.Value<string>();
                var toolName = toolsArray[i]["name"]?.Value<string>();
                Assert.Equal(capToolName, toolName);
            }
        }

        /// <summary>
        /// Health endpoints should be accessible without authentication
        /// even when API key is required.
        /// </summary>
        [Fact]
        public async Task HealthEndpoint_IsAccessibleWithoutApiKey()
        {
            // Create a new server with API key requirement
            using (var tempRepoWithAuth = new TempTestRepository())
            {
                var serverWithAuth = new McpTestServer(tempRepoWithAuth.Root, requireApiKey: true);
                var clientWithoutAuth = serverWithAuth.CreateHttpClient(includeApiKey: false);
                try
                {
                    var response = await clientWithoutAuth.GetAsync("/api/health");

                    // Health should still be 200 OK (no auth required)
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
                finally
                {
                    serverWithAuth?.Dispose();
                }
            }
        }

        /// <summary>
        /// Capabilities endpoint should be accessible without authentication.
        /// </summary>
        [Fact]
        public async Task CapabilitiesEndpoint_IsAccessibleWithoutApiKey()
        {
            using (var tempRepoWithAuth = new TempTestRepository())
            {
                var serverWithAuth = new McpTestServer(tempRepoWithAuth.Root, requireApiKey: true);
                var clientWithoutAuth = serverWithAuth.CreateHttpClient(includeApiKey: false);
                try
                {
                    var response = await clientWithoutAuth.GetAsync("/api/capabilities");

                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
                finally
                {
                    serverWithAuth?.Dispose();
                }
            }
        }

        /// <summary>
        /// All discovery endpoint responses should include proper ApiResponse envelope.
        /// </summary>
        [Theory]
        [InlineData("/api/health")]
        [InlineData("/api/health/detailed")]
        [InlineData("/api/version")]
        [InlineData("/api/capabilities")]
        [InlineData("/api/tools")]
        public async Task DiscoveryEndpoints_ReturnValidApiResponseEnvelope(string endpoint)
        {
            var response = await _client.GetAsync(endpoint);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            // Verify ApiResponse envelope structure
            Assert.NotNull(json["success"]);
            Assert.NotNull(json["data"]);
            Assert.NotNull(json["requestId"]);
            Assert.NotNull(json["timestamp"]);

            // success should be boolean true
            Assert.True(json["success"]?.Value<bool>());

            // data should not be null for successful response
            Assert.NotNull(json["data"]);
        }

        /// <summary>
        /// All discovery endpoints should have Content-Type: application/json
        /// </summary>
        [Theory]
        [InlineData("/api/health")]
        [InlineData("/api/capabilities")]
        [InlineData("/api/tools")]
        public async Task DiscoveryEndpoints_ReturnJsonContentType(string endpoint)
        {
            var response = await _client.GetAsync(endpoint);

            Assert.NotNull(response.Content.Headers.ContentType);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
        }

        /// <summary>
        /// GET /api/capabilities should return serviceName == "StewardessMCPService".
        /// Validates the fixed typo ("Service" → "Service").
        /// </summary>
        [Fact]
        public async Task GetCapabilities_ServiceName_IsCorrect()
        {
            var response = await _client.GetAsync("/api/capabilities");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);

            var manifest    = json["data"];
            var serviceName = manifest?
                .ToObject<Newtonsoft.Json.Linq.JObject>()
                ?.GetValue("serviceName", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>();

            Assert.Equal("StewardessMCPService", serviceName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// GET /api/capabilities should include a manifestFormat field
        /// that starts with "stewardess-".
        /// </summary>
        [Fact]
        public async Task GetCapabilities_ManifestFormat_IsPresent()
        {
            var response = await _client.GetAsync("/api/capabilities");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);

            var manifest       = json["data"] as JObject;
            var manifestFormat = manifest?
                .GetValue("manifestFormat", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>();

            Assert.NotNull(manifestFormat);
            Assert.StartsWith("stewardess-", manifestFormat, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// GET /api/capabilities should include a policies object with at least
        /// the approvalRequiredForDestructive field.
        /// </summary>
        [Fact]
        public async Task GetCapabilities_Policies_IsPresent()
        {
            var response = await _client.GetAsync("/api/capabilities");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);

            var manifest = json["data"] as JObject;
            var policies = manifest?.GetValue("policies", StringComparison.OrdinalIgnoreCase) as JObject;

            Assert.NotNull(policies);
            Assert.NotNull(policies.GetValue("approvalRequiredForDestructive", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// GET /api/tools — every tool must have a non-null, non-empty sideEffectClass.
        /// </summary>
        [Fact]
        public async Task GetTools_AllTools_HaveSideEffectClass()
        {
            var response = await _client.GetAsync("/api/tools");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);
            var tools    = json["data"] as JArray;

            Assert.NotNull(tools);
            Assert.NotEmpty(tools);

            foreach (var tool in tools)
            {
                var name           = tool["name"]?.Value<string>() ?? "(unknown)";
                var sideEffectClass = (tool as JObject)?
                    .GetValue("sideEffectClass", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>();

                Assert.True(
                    !string.IsNullOrEmpty(sideEffectClass),
                    $"Tool '{name}' has null/empty sideEffectClass");
            }
        }

        /// <summary>
        /// GET /api/tools — every tool must have riskLevel set to "low", "medium", or "high".
        /// </summary>
        [Fact]
        public async Task GetTools_AllTools_HaveRiskLevel()
        {
            var response = await _client.GetAsync("/api/tools");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);
            var tools    = json["data"] as JArray;

            Assert.NotNull(tools);
            Assert.NotEmpty(tools);

            var validLevels = new[] { "low", "medium", "high" };
            foreach (var tool in tools)
            {
                var name      = tool["name"]?.Value<string>() ?? "(unknown)";
                var riskLevel = (tool as JObject)?
                    .GetValue("riskLevel", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>();

                Assert.True(
                    validLevels.Contains(riskLevel, StringComparer.OrdinalIgnoreCase),
                    $"Tool '{name}' riskLevel '{riskLevel}' is not one of: {string.Join(", ", validLevels)}");
            }
        }

        /// <summary>
        /// GET /api/tools — every tool must have a tags array (may be empty, but not null).
        /// </summary>
        [Fact]
        public async Task GetTools_AllTools_HaveTags()
        {
            var response = await _client.GetAsync("/api/tools");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);
            var tools    = json["data"] as JArray;

            Assert.NotNull(tools);
            Assert.NotEmpty(tools);

            foreach (var tool in tools)
            {
                var name = tool["name"]?.Value<string>() ?? "(unknown)";
                var tags = (tool as JObject)?
                    .GetValue("tags", StringComparison.OrdinalIgnoreCase);

                Assert.True(
                    tags != null && tags.Type == JTokenType.Array,
                    $"Tool '{name}' is missing the tags array");
            }
        }

        /// <summary>
        /// GET /api/tools — every tool must have a non-null outputSchema.
        /// </summary>
        [Fact]
        public async Task GetTools_AllTools_HaveOutputSchema()
        {
            var response = await _client.GetAsync("/api/tools");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);
            var tools    = json["data"] as JArray;

            Assert.NotNull(tools);
            Assert.NotEmpty(tools);

            foreach (var tool in tools)
            {
                var name         = tool["name"]?.Value<string>() ?? "(unknown)";
                var outputSchema = (tool as JObject)?
                    .GetValue("outputSchema", StringComparison.OrdinalIgnoreCase);

                Assert.True(
                    outputSchema != null && outputSchema.Type != JTokenType.Null,
                    $"Tool '{name}' has null outputSchema");
            }
        }

        /// <summary>
        /// GET /api/tools — write_file must have sideEffectClass="file-write",
        /// riskLevel="medium", and supportsDryRun=true.
        /// </summary>
        [Fact]
        public async Task GetTools_WriteFile_IsFileWriteSideEffect()
        {
            var response = await _client.GetAsync("/api/tools");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);
            var tools    = json["data"] as JArray;

            Assert.NotNull(tools);

            var writeFile = tools
                .FirstOrDefault(t => string.Equals(
                    t["name"]?.Value<string>(), "write_file", StringComparison.OrdinalIgnoreCase))
                as JObject;

            Assert.NotNull(writeFile);

            var sideEffectClass = writeFile
                .GetValue("sideEffectClass", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            var riskLevel = writeFile
                .GetValue("riskLevel", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            var supportsDryRun = writeFile
                .GetValue("supportsDryRun", StringComparison.OrdinalIgnoreCase)?.Value<bool>();

            Assert.Equal("file-write", sideEffectClass, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("medium",     riskLevel,       StringComparer.OrdinalIgnoreCase);
            Assert.True(supportsDryRun == true);
        }

        /// <summary>
        /// GET /api/tools — run_build must have sideEffectClass="process-execution"
        /// and riskLevel "medium" or "high".
        /// </summary>
        [Fact]
        public async Task GetTools_RunCustomCommand_IsHighRisk()
        {
            var response = await _client.GetAsync("/api/tools");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);
            var tools    = json["data"] as JArray;

            Assert.NotNull(tools);

            // Prefer run_command if present, fall back to run_build.
            var tool = (tools
                .FirstOrDefault(t => string.Equals(
                    t["name"]?.Value<string>(), "run_command", StringComparison.OrdinalIgnoreCase))
                ?? tools.FirstOrDefault(t => string.Equals(
                    t["name"]?.Value<string>(), "run_build", StringComparison.OrdinalIgnoreCase)))
                as JObject;

            Assert.NotNull(tool);

            var sideEffectClass = tool
                .GetValue("sideEffectClass", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            var riskLevel = tool
                .GetValue("riskLevel", StringComparison.OrdinalIgnoreCase)?.Value<string>();

            Assert.Equal("process-execution", sideEffectClass, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(riskLevel, new[] { "medium", "high" }, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// GET /api/tools — search_text must have sideEffectClass="read-only"
        /// and riskLevel="low".
        /// </summary>
        [Fact]
        public async Task GetTools_SearchText_IsReadOnly()
        {
            var response = await _client.GetAsync("/api/tools");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);
            var tools    = json["data"] as JArray;

            Assert.NotNull(tools);

            var searchText = tools
                .FirstOrDefault(t => string.Equals(
                    t["name"]?.Value<string>(), "search_text", StringComparison.OrdinalIgnoreCase))
                as JObject;

            Assert.NotNull(searchText);

            var sideEffectClass = searchText
                .GetValue("sideEffectClass", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            var riskLevel = searchText
                .GetValue("riskLevel", StringComparison.OrdinalIgnoreCase)?.Value<string>();

            Assert.Equal("read-only", sideEffectClass, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("low",       riskLevel,       StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// GET /api/capabilities must include a non-null commonErrorSchema in the manifest.
        /// </summary>
        [Fact]
        public async Task GetCapabilities_CommonErrorSchema_IsPresent()
        {
            var response = await _client.GetAsync("/api/capabilities");
            var body     = await response.Content.ReadAsStringAsync();
            var json     = JObject.Parse(body);

            var manifest = json["data"] as JObject;
            Assert.NotNull(manifest);

            var errorSchema = manifest!
                .GetValue("commonErrorSchema", StringComparison.OrdinalIgnoreCase);

            Assert.NotNull(errorSchema);
            Assert.NotEqual(JTokenType.Null, errorSchema!.Type);
        }

        /// <summary>
        /// Calling code_index.get_dependencies without symbol_id must return
        /// an error object with Code == "ValidationError".
        /// </summary>
        [Fact]
        public async Task GetCapabilities_ToolErrors_NoRequiredSymbolId_ReturnsValidationError()
        {
            var (data, _) = await _client.CallToolAsync(
                "code_index.get_dependencies",
                new { });

            var errorObj = (data as JObject)?
                .GetValue("error", StringComparison.OrdinalIgnoreCase) as JObject;

            Assert.NotNull(errorObj);

            var code = errorObj!
                .GetValue("Code", StringComparison.OrdinalIgnoreCase)?.Value<string>();

            Assert.Equal("ValidationError", code, StringComparer.OrdinalIgnoreCase);
        }
    }
}
