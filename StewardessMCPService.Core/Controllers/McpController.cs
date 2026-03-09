using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StewardessMCPService.Configuration;
using StewardessMCPService.Mcp;
using StewardessMCPService.Models;
using StewardessMCPService.Services;

namespace StewardessMCPService.Controllers
{
    /// <summary>
    /// JSON-RPC 2.0 MCP endpoint.
    ///
    ///   POST /mcp/v1/           — dispatch any JSON-RPC method (tools/list, tools/call, ping, …)
    ///   GET  /mcp/v1/manifest   — machine-readable capability manifest
    ///   GET  /mcp/v1/tools      — convenience alias for tools/list (non-RPC)
    /// </summary>
    [Route("mcp/v1")]
    public sealed class McpController : BaseController
    {
        private McpToolHandler   Handler   => GetService<McpToolHandler>();
        private McpToolRegistry  Registry  => GetService<McpToolRegistry>();
        private McpServiceSettings Settings => GetService<McpServiceSettings>();
        private IGitService      GitService => GetService<IGitService>();

        // ── POST /mcp/v1/ ────────────────────────────────────────────────────────

        /// <summary>
        /// Main JSON-RPC 2.0 dispatch endpoint.
        /// Accepts a single request object; batch requests are not supported.
        /// </summary>
        [HttpPost, Route("")]
        public async Task<IActionResult> Dispatch(
            [FromBody] JObject body, CancellationToken ct)
        {
            if (body == null)
            {
                var errResp = McpResponse.Err(null, McpErrorCodes.ParseError, "Request body is missing or not valid JSON.");
                return JsonResponse(errResp);
            }

            McpRequest request;
            try
            {
                request = body.ToObject<McpRequest>();
            }
            catch (Exception ex)
            {
                var errResp = McpResponse.Err(null, McpErrorCodes.ParseError, $"Failed to parse request: {ex.Message}");
                return JsonResponse(errResp);
            }

            var response = await Handler.DispatchAsync(request, ct).ConfigureAwait(false);

            // Per JSON-RPC spec: notifications (requests without an id) MUST NOT receive
            // a response.  DispatchAsync returns null for notifications.
            if (response == null)
                return StatusCode(202);

            // Return 200 for method-not-found and similar app-level errors per JSON-RPC spec.
            return JsonResponse(response);
        }

        // ── GET /mcp/v1/tools ───────────────────────────────────────────────────

        /// <summary>
        /// Convenience endpoint: returns all registered tool definitions without
        /// requiring a JSON-RPC envelope.
        /// </summary>
        [HttpGet, Route("tools")]
        public IActionResult ListTools()
        {
            var tools = Registry.GetAllDefinitions();
            return Ok(new McpListToolsResult { Tools = new System.Collections.Generic.List<McpToolDefinition>(tools) });
        }

        // ── GET /mcp/v1/manifest ────────────────────────────────────────────────

        /// <summary>
        /// Returns a machine-readable capability manifest.  An agent can fetch
        /// this once per session to discover all tools and server constraints
        /// without issuing multiple JSON-RPC calls.
        /// </summary>
        [HttpGet, Route("manifest")]
        public async Task<IActionResult> GetManifest(CancellationToken ct)
        {
            var tools = Registry.GetAllDefinitions();

            // Best-effort git context — failures are swallowed.
            bool  isGit  = false;
            string branch = null;
            try
            {
                isGit  = await GitService.IsGitRepositoryAsync(ct).ConfigureAwait(false);
                if (isGit)
                {
                    var status = await GitService.GetStatusAsync(new GitStatusRequest(), ct).ConfigureAwait(false);
                    branch = status?.CurrentBranch;
                }
            }
            catch { /* best-effort */ }

            var manifest = new McpCapabilitiesManifest
            {
                SchemaVersion  = "1.0",
                ServiceName    = "StewardessMCPService",
                ServiceVersion = Settings.ServiceVersion,
                GeneratedAt    = DateTimeOffset.UtcNow,

                Capabilities = new McpServerCapabilities
                {
                    CanRead              = true,
                    CanWrite             = !Settings.ReadOnlyMode,
                    CanSearch            = true,
                    CanExecuteCommands   = !Settings.ReadOnlyMode,
                    CanAccessGit         = true,
                    SupportsDryRun       = true,
                    SupportsRollback     = true,
                    SupportsAuditLog     = Settings.EnableAuditLog,
                    SupportsBatchEdits   = true
                },

                Constraints = new McpServerConstraints
                {
                    MaxFileReadBytes            = Settings.MaxFileReadBytes,
                    MaxSearchResults            = Settings.MaxSearchResults,
                    MaxDirectoryDepth           = Settings.MaxDirectoryDepth,
                    MaxCommandExecutionSeconds  = Settings.MaxCommandExecutionSeconds,
                    AllowedCommands             = Settings.AllowedCommands,
                    BlockedFolders              = Settings.BlockedFolders,
                    BlockedExtensions           = Settings.BlockedExtensions
                },

                RepositoryContext = new McpRepositoryContext
                {
                    RepositoryName  = System.IO.Path.GetFileName(Settings.RepositoryRoot.TrimEnd('\\', '/')),
                    // Do not expose the full server-side absolute path; return only the leaf name.
                    RepositoryRoot  = System.IO.Path.GetFileName(Settings.RepositoryRoot.TrimEnd('\\', '/')),
                    IsGitRepository = isGit,
                    CurrentBranch   = branch
                },

                Tools = new System.Collections.Generic.List<McpToolDefinition>(tools)
            };

            return Ok(manifest);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Serialises <paramref name="obj"/> as JSON with HTTP 200.</summary>
        private IActionResult JsonResponse(object obj)
        {
            var json = JsonConvert.SerializeObject(obj, Formatting.None);
            return Content(json, "application/json", System.Text.Encoding.UTF8);
        }
    }
}
