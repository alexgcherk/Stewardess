using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using StewardessMCPService.IntegrationTests.Helpers;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    [Collection(IntegrationTestCollection.Name)]
    /// <summary>
    /// Integration tests that verify every OpenAPI operation in the live Swagger spec
    /// carries a valid, unique <c>operationId</c>.
    ///
    /// Open WebUI (and other OpenAPI consumers) silently skip operations without an
    /// <c>operationId</c>.  These tests act as a regression guard ensuring the
    /// Swashbuckle <c>CustomOperationIds</c> configuration remains in place.
    /// </summary>
    public sealed class OpenApiOperationIdTests : IDisposable
    {
        private const string SwaggerSpecPath = "/swagger/v1/swagger.json";

        private readonly McpTestServer  _server;
        private readonly McpRestClient  _client;

        public OpenApiOperationIdTests()
        {
            _server = new McpTestServer();
            _client = _server.CreateHttpClient();
        }

        public void Dispose() => _server?.Dispose();

        // ── Spec accessibility ───────────────────────────────────────────────────

        /// <summary>GET /swagger/v1/swagger.json must return 200 with JSON content.</summary>
        [Fact]
        public async Task GetSwaggerSpec_Returns200WithJson()
        {
            var response = await _client.GetAsync(SwaggerSpecPath);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            Assert.Contains("json", contentType, StringComparison.OrdinalIgnoreCase);

            var body = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrWhiteSpace(body), "Swagger spec body must not be empty.");
        }

        /// <summary>The spec must be valid JSON and contain the OpenAPI "paths" object.</summary>
        [Fact]
        public async Task SwaggerSpec_IsValidJsonWithPathsObject()
        {
            var spec = await FetchSpecAsync();
            Assert.NotNull(spec["paths"]);
        }

        // ── operationId presence ─────────────────────────────────────────────────

        /// <summary>Every HTTP operation in every path must have a non-empty operationId.</summary>
        [Fact]
        public async Task AllOperations_HaveNonEmptyOperationId()
        {
            var (ops, missing) = await CollectOperationsAsync();

            Assert.True(missing.Count == 0,
                $"{missing.Count} operation(s) are missing operationId:\n" +
                string.Join("\n", missing));
        }

        /// <summary>All operationIds across the entire spec must be unique (no duplicates).</summary>
        [Fact]
        public async Task AllOperationIds_AreUnique()
        {
            var (ops, _) = await CollectOperationsAsync();

            var duplicates = ops
                .GroupBy(o => o.OperationId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(duplicates.Count == 0,
                $"Duplicate operationIds found: {string.Join(", ", duplicates)}");
        }

        // ── operationId format ───────────────────────────────────────────────────

        /// <summary>
        /// Every operationId must follow the {Controller}_{Action} format:
        /// PascalCase segments separated by a single underscore.
        /// </summary>
        [Fact]
        public async Task AllOperationIds_FollowControllerActionFormat()
        {
            var (ops, _) = await CollectOperationsAsync();

            var violations = ops
                .Where(o => !IsValidOperationId(o.OperationId))
                .Select(o => $"  {o.Method.ToUpper()} {o.Path} → '{o.OperationId}'")
                .ToList();

            Assert.True(violations.Count == 0,
                $"OperationIds not following 'Controller_Action' format:\n" +
                string.Join("\n", violations));
        }

        // ── expected repo_browser operation IDs ─────────────────────────────────

        /// <summary>
        /// The four repo_browser endpoints must appear in the spec with their
        /// expected operationIds.
        /// </summary>
        [Theory]
        [InlineData("RepoBrowser_PrintTree")]
        [InlineData("RepoBrowser_Grep")]
        [InlineData("RepoBrowser_ReadFile")]
        [InlineData("RepoBrowser_FindPath")]
        public async Task RepoBrowserOperations_HaveExpectedOperationId(string expectedId)
        {
            var (ops, _) = await CollectOperationsAsync();

            var found = ops.Any(o => string.Equals(o.OperationId, expectedId, StringComparison.OrdinalIgnoreCase));
            Assert.True(found, $"OperationId '{expectedId}' was not found in the spec. " +
                               $"Available IDs: {string.Join(", ", ops.Select(o => o.OperationId))}");
        }

        /// <summary>
        /// Core file/search/git/command operation IDs must also be present,
        /// proving the fix applies globally (not just to the new controller).
        /// </summary>
        [Theory]
        [InlineData("File_ReadFile")]
        [InlineData("Search_SearchTextGet")]
        [InlineData("Git_GetStatus")]
        [InlineData("Command_RunBuild")]
        [InlineData("Repository_GetRepositoryInfo")]
        public async Task CoreOperations_HaveExpectedOperationId(string expectedId)
        {
            var (ops, _) = await CollectOperationsAsync();

            var found = ops.Any(o => string.Equals(o.OperationId, expectedId, StringComparison.OrdinalIgnoreCase));
            Assert.True(found, $"OperationId '{expectedId}' was not found in the spec.");
        }

        // ── minimum operation count ──────────────────────────────────────────────

        /// <summary>
        /// The spec must contain at least 41 operations (37 pre-existing + 4 new repo_browser ones).
        /// </summary>
        [Fact]
        public async Task Spec_HasAtLeast41Operations()
        {
            var (ops, _) = await CollectOperationsAsync();
            Assert.True(ops.Count >= 41, $"Expected ≥41 operations but found {ops.Count}.");
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private async Task<JObject> FetchSpecAsync()
        {
            var response = await _client.GetAsync(SwaggerSpecPath);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return JObject.Parse(body);
        }

        private async Task<(List<OperationRecord> All, List<string> MissingIds)> CollectOperationsAsync()
        {
            var spec  = await FetchSpecAsync();
            var paths = spec["paths"] as JObject;
            Assert.NotNull(paths);

            var all     = new List<OperationRecord>();
            var missing = new List<string>();

            var httpMethods = new[] { "get", "post", "put", "patch", "delete", "head", "options" };

            foreach (var pathProp in paths.Properties())
            {
                var pathItem = pathProp.Value as JObject;
                if (pathItem == null) continue;

                foreach (var method in httpMethods)
                {
                    var operation = pathItem[method] as JObject;
                    if (operation == null) continue;

                    var opId = operation["operationId"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(opId))
                        missing.Add($"  {method.ToUpper()} {pathProp.Name}");
                    else
                        all.Add(new OperationRecord(pathProp.Name, method, opId));
                }
            }

            return (all, missing);
        }

        private static bool IsValidOperationId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var parts = id.Split('_');
            // Must have exactly one underscore separating two non-empty PascalCase segments
            return parts.Length == 2 &&
                   !string.IsNullOrEmpty(parts[0]) &&
                   !string.IsNullOrEmpty(parts[1]) &&
                   char.IsUpper(parts[0][0]) &&
                   char.IsUpper(parts[1][0]);
        }

        private sealed record OperationRecord(string Path, string Method, string OperationId);
    }
}
