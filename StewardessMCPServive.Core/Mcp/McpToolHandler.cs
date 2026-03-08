using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Mcp
{
    /// <summary>
    /// JSON-RPC 2.0 dispatcher that implements the MCP specification (protocol version 2024-11-05).
    ///
    /// Supported methods:
    ///   initialize           — capability negotiation (required lifecycle step)
    ///   notifications/*      — silently consumed (no response per JSON-RPC spec)
    ///   ping                 — liveness check
    ///   tools/list           — enumerate all registered tools
    ///   tools/call           — invoke a named tool with structured arguments
    /// </summary>
    public sealed class McpToolHandler
    {
        private readonly McpToolRegistry _registry;
        private readonly string _serverVersion;
        private static readonly McpLogger _log = McpLogger.For<McpToolHandler>();

        /// <summary>MCP protocol version this server implements.</summary>
        public const string ProtocolVersion = "2024-11-05";

        /// <summary>Initialises a new instance of <see cref="McpToolHandler"/>.</summary>
        public McpToolHandler(McpToolRegistry registry, string serverVersion = "1.0.0")
        {
            _registry      = registry      ?? throw new ArgumentNullException(nameof(registry));
            _serverVersion = serverVersion ?? "1.0.0";
        }

        // ── Dispatch ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Dispatches a JSON-RPC 2.0 request to the appropriate handler.
        ///
        /// Per the JSON-RPC spec, requests WITHOUT an <c>id</c> are notifications —
        /// the server MUST NOT return any response to them (return null signals this
        /// to the caller).  Returns null for notifications; always returns a well-formed
        /// <see cref="McpResponse"/> for requests (id present).  Never throws.
        /// </summary>
        public async Task<McpResponse?> DispatchAsync(McpRequest? request, CancellationToken ct = default)
        {
            if (request == null)
                return McpResponse.Err(null, McpErrorCodes.InvalidRequest, "Request body is null.");

            if (request.JsonRpc != "2.0")
                return McpResponse.Err(request.Id, McpErrorCodes.InvalidRequest,
                    $"Invalid jsonrpc version: '{request.JsonRpc}'. Expected '2.0'.");

            if (string.IsNullOrWhiteSpace(request.Method))
                return McpResponse.Err(request.Id, McpErrorCodes.InvalidRequest, "Method is required.");

            // JSON-RPC notifications: requests without an id. The spec says the server MUST
            // NOT return any response.  We consume them silently and return null.
            // The controller will check for null and skip writing a response body.
            if (request.Id == null && (
                request.Method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase) ||
                request.Method.Equals("initialized", StringComparison.OrdinalIgnoreCase)))
            {
                _log.Debug($"MCP notification received (no response sent): {request.Method}");
                return null;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                McpResponse response;

                switch (request.Method.ToLowerInvariant())
                {
                    case "initialize":
                        response = HandleInitialize(request);
                        break;

                    case "ping":
                        response = HandlePing(request);
                        break;

                    case "tools/list":
                        response = HandleToolsList(request);
                        break;

                    case "tools/call":
                        response = await HandleToolsCallAsync(request, ct).ConfigureAwait(false);
                        break;

                    default:
                        response = McpResponse.Err(request.Id, McpErrorCodes.MethodNotFound,
                            $"Unknown method: {request.Method}");
                        break;
                }

                _log.LogToolCall(request.Method, null, response.Error == null, sw.ElapsedMilliseconds);
                return response;
            }
            catch (OperationCanceledException)
            {
                return McpResponse.Err(request.Id, McpErrorCodes.TimeoutExceeded, "The operation was cancelled.");
            }
            catch (Exception ex)
            {
                _log.Error($"Unhandled exception in McpToolHandler.DispatchAsync for method '{request.Method}'", ex);
                return McpResponse.Err(request.Id, McpErrorCodes.InternalError, "Internal server error.");
            }
        }

        // ── Method handlers ──────────────────────────────────────────────────────

        /// <summary>
        /// Handles the <c>initialize</c> lifecycle request.
        /// Negotiates protocol version and declares server capabilities.
        /// The server accepts any client protocol version — it advertises 2024-11-05.
        /// </summary>
        private McpResponse HandleInitialize(McpRequest request)
        {
            var result = new McpInitializeResult
            {
                ProtocolVersion = ProtocolVersion,
                ServerInfo = new McpServerInfo
                {
                    Name    = "StewardessMCPServive",
                    Version = _serverVersion
                },
                Capabilities = new McpInitializeServerCapabilities
                {
                    Tools   = new McpToolsCapability { ListChanged = false },
                    Logging = null   // not yet implemented
                },
                Instructions = "This is a local source-code repository MCP service. " +
                               "Use tools/list to discover all available tools for reading, " +
                               "searching, editing, and validating code in the repository."
            };
            return McpResponse.Ok(request.Id, result);
        }

        private static McpResponse HandlePing(McpRequest request) =>
            McpResponse.Ok(request.Id, new McpPingResult
            {
                Status         = "ok",
                Timestamp      = DateTimeOffset.UtcNow,
                ServiceVersion = "1.0.0"
            });

        private McpResponse HandleToolsList(McpRequest request)
        {
            var tools = _registry.GetAllDefinitions();

            // MCP spec supports cursor-based pagination for tools/list.
            // Extract optional cursor from params (may be null for first page).
            string? cursor = null;
            try
            {
                var p = DeserializeParams<System.Collections.Generic.Dictionary<string, object>>(request.Params);
                if (p != null && p.TryGetValue("cursor", out var c) && c != null)
                    cursor = c.ToString();
            }
            catch { /* no cursor = first page */ }

            // Determine starting offset from cursor (cursor encodes an integer offset).
            int offset = 0;
            if (!string.IsNullOrEmpty(cursor))
                int.TryParse(cursor, out offset);

            const int PageSize = 50;
            var page = tools.Skip(offset).Take(PageSize).ToList();
            int nextOffset = offset + page.Count;
            string? nextCursor = nextOffset < tools.Count ? nextOffset.ToString() : null;

            return McpResponse.Ok(request.Id, new McpListToolsResult
            {
                Tools      = page,
                NextCursor = nextCursor
            });
        }

        private async Task<McpResponse> HandleToolsCallAsync(McpRequest request, CancellationToken ct)
        {
            // Deserialize the params into McpToolCallParams.
            McpToolCallParams? callParams;
            try
            {
                callParams = DeserializeParams<McpToolCallParams>(request.Params);
            }
            catch (Exception ex)
            {
                return McpResponse.Err(request.Id, McpErrorCodes.InvalidParams,
                    $"Invalid params for tools/call: {ex.Message}");
            }

            if (callParams == null || string.IsNullOrWhiteSpace(callParams.Name))
                return McpResponse.Err(request.Id, McpErrorCodes.InvalidParams, "params.name is required.");

            if (!_registry.TryGetDefinition(callParams.Name, out var definition))
                return McpResponse.Err(request.Id, McpErrorCodes.ToolNotFound,
                    $"Tool not found: {callParams.Name}");

            if (definition!.IsDisabled)
                return McpResponse.Err(request.Id, McpErrorCodes.ReadOnlyMode,
                    $"Tool '{callParams.Name}' is disabled: {definition.DisabledReason}");

            try
            {
                var result = await _registry.InvokeAsync(
                    callParams.Name,
                    callParams.Arguments ?? new Dictionary<string, object>(),
                    ct).ConfigureAwait(false);

                return McpResponse.Ok(request.Id, result);
            }
            catch (KeyNotFoundException)
            {
                return McpResponse.Err(request.Id, McpErrorCodes.ToolNotFound,
                    $"Tool not found: {callParams.Name}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return McpResponse.Err(request.Id, McpErrorCodes.Forbidden, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return McpResponse.Err(request.Id, McpErrorCodes.InvalidParams, ex.Message);
            }
            catch (OperationCanceledException)
            {
                return McpResponse.Err(request.Id, McpErrorCodes.TimeoutExceeded,
                    "The tool call timed out.");
            }
            catch (Exception ex)
            {
                _log.Error($"Tool '{callParams.Name}' threw an unhandled exception", ex);
                return McpResponse.Err(request.Id, McpErrorCodes.InternalError,
                    $"Tool execution failed: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Deserializes a params object (which may be a JObject, Dictionary, or already typed) 
        /// into the target type using JSON round-trip conversion.
        /// </summary>
        private static T? DeserializeParams<T>(object? raw) where T : class
        {
            if (raw == null) return default;
            if (raw is T typed) return typed;
            if (raw is JObject jobj) return jobj.ToObject<T>();
            var json = JsonConvert.SerializeObject(raw);
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
