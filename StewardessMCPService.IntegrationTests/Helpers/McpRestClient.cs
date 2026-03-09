using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using StewardessMCPService.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StewardessMCPService.IntegrationTests.Helpers
{
    /// <summary>
    /// Typed HTTP client over the MCP service REST API.
    /// Wraps the common endpoints used by integration tests so individual tests
    /// do not have to construct raw HTTP calls.
    /// </summary>
    public sealed class McpRestClient
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;

        /// <summary>Creates a client backed by the given <see cref="HttpClient"/>.</summary>
        public McpRestClient(HttpClient httpClient, string apiKey = null)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey;
        }

        // ── MCP JSON-RPC tool calls ──────────────────────────────────────────────

        /// <summary>
        /// Invokes a named MCP tool via JSON-RPC 2.0 (<c>POST /mcp/v1/</c>).
        /// Returns a tuple of the parsed content JObject and a flag indicating
        /// whether the tool itself reported an application-level error.
        /// Throws <see cref="InvalidOperationException"/> for JSON-RPC protocol errors.
        /// </summary>
        /// <param name="toolName">Name of the MCP tool to invoke.</param>
        /// <param name="arguments">
        /// Tool arguments; serialized to a JSON object. May be null for tools
        /// that take no required parameters.
        /// </param>
        public async Task<(JObject Data, bool IsError)> CallToolAsync(
            string toolName, object arguments = null)
        {
            var requestBody = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"]      = 1,
                ["method"]  = "tools/call",
                ["params"]  = JObject.FromObject(new
                {
                    name      = toolName,
                    arguments = arguments != null
                        ? JObject.FromObject(arguments)
                        : new JObject(),
                }),
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "mcp/v1/")
            {
                Content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json"),
            };
            AddApiKeyIfNeeded(request);

            var response = await _http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            var rpc = JObject.Parse(raw);

            // McpController.JsonResponse uses default Newtonsoft.Json (PascalCase).
            // Use case-insensitive lookups to handle both PascalCase and camelCase.
            var errorToken = rpc.GetValue("error", StringComparison.OrdinalIgnoreCase);
            if (errorToken != null && errorToken.Type != JTokenType.Null)
                throw new InvalidOperationException(
                    $"JSON-RPC protocol error calling '{toolName}': {errorToken}");

            var result     = rpc.GetValue("result", StringComparison.OrdinalIgnoreCase) as JObject;
            var isError    = result?.GetValue("isError", StringComparison.OrdinalIgnoreCase)?.Value<bool>() ?? false;
            var contentArr = result?.GetValue("content", StringComparison.OrdinalIgnoreCase) as JArray;
            var text       = (contentArr?[0] as JObject)?
                                 .GetValue("text", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            var data       = text != null ? JObject.Parse(text) : new JObject();

            return (data, isError);
        }

        // ── Edit operations ──────────────────────────────────────────────────────

        /// <summary>Creates a directory at <paramref name="relativePath"/>.</summary>
        public async Task<EditResult> CreateDirectoryAsync(string relativePath)
        {
            var (data, isError) = await CallToolAsync("create_directory", new { path = relativePath, create_parents = true });
            if (isError)
                return new EditResult { Success = false, ErrorMessage = data.GetValue("message", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? "create_directory failed" };
            return new EditResult { Success = true, Operation = "create_directory", RelativePath = relativePath };
        }

        /// <summary>Writes <paramref name="content"/> to the file at <paramref name="relativePath"/>.</summary>
        public async Task<EditResult> WriteFileAsync(string relativePath, string content)
        {
            var (data, isError) = await CallToolAsync("write_file", new { path = relativePath, content = content });
            if (isError)
                return new EditResult { Success = false, ErrorMessage = data.GetValue("message", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? "write_file failed" };
            return new EditResult { Success = true, RelativePath = relativePath };
        }

        // ── Command operations ───────────────────────────────────────────────────

        /// <summary>
        /// Invokes <c>dotnet build &lt;projectRelativePath&gt;</c> via the
        /// <c>POST /api/command/build</c> endpoint.
        /// </summary>
        public Task<CommandResult> BuildAsync(
            string projectRelativePath,
            string configuration = "Debug") =>
            PostAsync<RunBuildRequest, CommandResult>(
                "api/command/build",
                new RunBuildRequest
                {
                    BuildCommand  = "dotnet build " + projectRelativePath,
                    Configuration = configuration
                });

        /// <summary>
        /// Runs an arbitrary command via <c>POST /api/command/run</c>.
        /// The command must start with a prefix present in the server's
        /// AllowedCommands list.
        /// </summary>
        public Task<CommandResult> RunCommandAsync(
            string command,
            string workingDirectory = "") =>
            PostAsync<RunCustomCommandRequest, CommandResult>(
                "api/command/run",
                new RunCustomCommandRequest
                {
                    Command          = command,
                    WorkingDirectory = workingDirectory
                });

        // ── Health ───────────────────────────────────────────────────────────────

        /// <summary>Returns the raw JSON response body from <c>GET /api/health</c>.</summary>
        public async Task<string> GetHealthAsync()
        {
            var response = await _http.GetAsync("api/health");
            return await response.Content.ReadAsStringAsync();
        }

        // ── Internal helpers ─────────────────────────────────────────────────────

        /// <summary>Adds the API key to request headers if set.</summary>
        private void AddApiKeyIfNeeded(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_apiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            }
        }

        private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body)
        {
            var json     = JsonConvert.SerializeObject(body);
            var content  = new StringContent(json, Encoding.UTF8, "application/json");
            var request  = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            AddApiKeyIfNeeded(request);
            var response = await _http.SendAsync(request);
            var raw      = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"POST {url} → HTTP {(int)response.StatusCode}\n{raw}");

            var envelope = JsonConvert.DeserializeObject<ApiResponse<TResponse>>(raw);
            if (envelope == null)
                throw new InvalidOperationException(
                    $"POST {url} returned a null envelope.\n{raw}");
            if (!envelope.Success)
                throw new InvalidOperationException(
                    $"POST {url} returned success=false: {envelope.Error?.Message}\n{raw}");

            return envelope.Data;
        }

        /// <summary>Issues a GET request to the given path with API key auth if configured.</summary>
        public async Task<HttpResponseMessage> GetAsync(string path)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            AddApiKeyIfNeeded(request);
            return await _http.SendAsync(request);
        }
    }
}
