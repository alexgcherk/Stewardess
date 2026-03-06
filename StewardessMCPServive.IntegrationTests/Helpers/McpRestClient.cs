using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using StewardessMCPServive.Models;
using Newtonsoft.Json;

namespace StewardessMCPServive.IntegrationTests.Helpers
{
    /// <summary>
    /// Typed HTTP client over the MCP service REST API.
    /// Wraps the common endpoints used by integration tests so individual tests
    /// do not have to construct raw HTTP calls.
    /// </summary>
    public sealed class McpRestClient
    {
        private readonly HttpClient _http;

        /// <summary>Creates a client backed by the given <see cref="HttpClient"/>.</summary>
        public McpRestClient(HttpClient httpClient)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        // ── Edit operations ──────────────────────────────────────────────────────

        /// <summary>Creates a directory at <paramref name="relativePath"/>.</summary>
        public Task<EditResult> CreateDirectoryAsync(string relativePath) =>
            PostAsync<CreateDirectoryRequest, EditResult>(
                "api/edit/create-directory",
                new CreateDirectoryRequest { Path = relativePath, CreateParents = true });

        /// <summary>
        /// Writes <paramref name="content"/> to the file at
        /// <paramref name="relativePath"/>, creating it when absent.
        /// </summary>
        public Task<EditResult> WriteFileAsync(string relativePath, string content) =>
            PostAsync<WriteFileRequest, EditResult>(
                "api/edit/write",
                new WriteFileRequest { Path = relativePath, Content = content });

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

        private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body)
        {
            var json     = JsonConvert.SerializeObject(body);
            var content  = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);
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
    }
}
