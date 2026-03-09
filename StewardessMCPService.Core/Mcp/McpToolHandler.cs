// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;

namespace StewardessMCPService.Mcp
{
    /// <summary>
    /// JSON-RPC 2.0 dispatcher that implements the MCP specification (protocol version 2025-03-26).
    ///
    /// Supported methods:
    ///   initialize              — capability negotiation (required lifecycle step)
    ///   notifications/*         — silently consumed (no response per JSON-RPC spec)
    ///   notifications/cancelled — cancels an in-flight request via its session
    ///   ping                    — liveness check
    ///   tools/list              — enumerate all registered tools
    ///   tools/call              — invoke a named tool with structured arguments
    ///
    /// Session &amp; progress
    /// ─────────────────────
    /// When an <see cref="IMcpSessionManager"/> is provided (Streamable HTTP transport),
    /// the handler:
    ///   • Creates a session on <c>initialize</c> and returns the session ID.
    ///   • Registers in-flight requests so <c>notifications/cancelled</c> can abort them.
    ///   • Honours <c>_meta.progressToken</c> by installing an <see cref="IProgress{T}"/>
    ///     implementation into <see cref="McpProgressContext"/> before invoking the tool.
    /// </summary>
    public sealed class McpToolHandler
    {
        private readonly McpToolRegistry       _registry;
        private readonly string                _serverVersion;
        private readonly IMcpSessionManager?   _sessions;
        private static readonly McpLogger      _log = McpLogger.For<McpToolHandler>();

        /// <summary>
        /// MCP protocol version this server implements.
        /// Updated to 2025-03-26 (Streamable HTTP transport).
        /// </summary>
        public const string ProtocolVersion = "2025-03-26";

        /// <summary>Initialises a new instance of <see cref="McpToolHandler"/>.</summary>
        public McpToolHandler(McpToolRegistry registry, string serverVersion = "1.0.0")
            : this(registry, serverVersion, null) { }

        /// <summary>
        /// Initialises a new instance of <see cref="McpToolHandler"/> with optional session support.
        /// </summary>
        /// <param name="registry">The tool registry containing all available tools.</param>
        /// <param name="serverVersion">Version string returned in initialize responses.</param>
        /// <param name="sessions">
        /// Optional session manager for the Streamable HTTP transport.
        /// When provided, <c>initialize</c> creates a session and returns a session ID,
        /// and <c>notifications/cancelled</c> can abort in-flight tool calls.
        /// Pass <c>null</c> for the classic HTTP transport (backward compatible).
        /// </param>
        public McpToolHandler(McpToolRegistry registry, string serverVersion, IMcpSessionManager? sessions)
        {
            _registry      = registry      ?? throw new ArgumentNullException(nameof(registry));
            _serverVersion = serverVersion ?? "1.0.0";
            _sessions      = sessions;
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
        public Task<McpResponse> DispatchAsync(McpRequest request, CancellationToken ct = default)
            => DispatchAsync(request, sessionId: null, progressToken: null, ct);

        /// <summary>
        /// Dispatches a JSON-RPC 2.0 request, optionally wiring up session-based
        /// cancellation and progress reporting for the Streamable HTTP transport.
        /// </summary>
        /// <param name="request">The parsed JSON-RPC request.</param>
        /// <param name="sessionId">
        /// Optional session ID from the <c>Mcp-Session-Id</c> header.
        /// Used to register in-flight requests for cancellation and to route
        /// session-creation responses.
        /// </param>
        /// <param name="progressToken">
        /// Optional progress token from <c>params._meta.progressToken</c>.
        /// When provided, the handler installs an <see cref="IProgress{T}"/> into
        /// <see cref="McpProgressContext"/> before calling the tool so that
        /// <c>notifications/progress</c> events can be sent back to the caller.
        /// </param>
        /// <param name="progressCallback">
        /// Callback invoked for each progress event when <paramref name="progressToken"/>
        /// is non-null.  Receives the fully-constructed progress notification.
        /// May be null even when a token is present (events are silently dropped).
        /// </param>
        /// <param name="ct">Cancellation token from the HTTP request context.</param>
        public async Task<McpResponse> DispatchAsync(
            McpRequest             request,
            string?                sessionId,
            string?                progressToken,
            CancellationToken      ct = default,
            Action<McpProgressNotification>? progressCallback = null)
        {
            if (request == null)
                return McpResponse.Err(null!, McpErrorCodes.InvalidRequest, "Request body is null.");

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

                // MCP 2025-03-26: notifications/cancelled asks the server to abort an
                // in-flight request.  We handle it specially even though no response is sent.
                if (request.Method.Equals("notifications/cancelled", StringComparison.OrdinalIgnoreCase))
                    HandleCancelled(request);

                return null!;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                McpResponse response;

                switch (request.Method.ToLowerInvariant())
                {
                    case "initialize":
                        response = HandleInitialize(request, sessionId);
                        break;

                    case "ping":
                        response = HandlePing(request);
                        break;

                    case "tools/list":
                        response = HandleToolsList(request);
                        break;

                    case "tools/call":
                        response = await HandleToolsCallAsync(
                            request, sessionId, progressToken, progressCallback, ct)
                            .ConfigureAwait(false);
                        break;

                    default:
                        response = McpResponse.Err(request.Id!, McpErrorCodes.MethodNotFound,
                            $"Unknown method: {request.Method}");
                        break;
                }

                _log.LogToolCall(request.Method, null!, response.Error == null, sw.ElapsedMilliseconds);
                return response;
            }
            catch (OperationCanceledException)
            {
                return McpResponse.Err(request.Id!, McpErrorCodes.TimeoutExceeded, "The operation was cancelled.");
            }
            catch (Exception ex)
            {
                _log.Error($"Unhandled exception in McpToolHandler.DispatchAsync for method '{request.Method}'", ex);
                return McpResponse.Err(request.Id!, McpErrorCodes.InternalError, "Internal server error.");
            }
        }

        // ── Method handlers ──────────────────────────────────────────────────────

        /// <summary>
        /// Handles the <c>initialize</c> lifecycle request.
        /// Negotiates protocol version and declares server capabilities.
        /// When a session manager is configured a new session is created and its
        /// ID is embedded in the result so the controller can return it as the
        /// <c>Mcp-Session-Id</c> response header.
        /// </summary>
        private McpResponse HandleInitialize(McpRequest request, string? existingSessionId)
        {
            // Parse client info so we can label the session.
            string? clientName = null;
            string  clientProtocol = ProtocolVersion;
            try
            {
                var p = DeserializeParams<McpInitializeParams>(request.Params!);
                clientName     = p?.ClientInfo?.Name;
                clientProtocol = p?.ProtocolVersion ?? ProtocolVersion;
            }
            catch { /* tolerate missing params */ }

            // Create a session if the session manager is available and none exists yet.
            string? sessionId = existingSessionId;
            if (_sessions != null && string.IsNullOrEmpty(sessionId))
            {
                var session = _sessions.CreateSession(clientProtocol, clientName);
                sessionId = session.Id;
            }

            var result = new McpInitializeResult
            {
                ProtocolVersion = ProtocolVersion,
                ServerInfo = new McpServerInfo
                {
                    Name    = "StewardessMCPService",
                    Version = _serverVersion
                },
                Capabilities = new McpInitializeServerCapabilities
                {
                    Tools   = new McpToolsCapability { ListChanged = false },
                    Logging = null!   // not yet implemented
                },
                Instructions = "This is a local source-code repository MCP service. " +
                               "Use tools/list to discover all available tools for reading, " +
                               "searching, editing, and validating code in the repository.",
                // Embed the session ID in the result so the controller can extract it
                // without re-parsing the response.  The controller will also send it
                // as the Mcp-Session-Id response header.
                SessionId = sessionId,
            };
            return McpResponse.Ok(request.Id!, result);
        }

        private static McpResponse HandlePing(McpRequest request) =>
            McpResponse.Ok(request.Id!, new McpPingResult
            {
                Status         = "ok",
                Timestamp      = DateTimeOffset.UtcNow,
                ServiceVersion = "1.0.0"
            });

        /// <summary>
        /// Processes a <c>notifications/cancelled</c> notification.
        /// Per the JSON-RPC spec no response is sent, but we try to cancel the
        /// in-flight request identified by the notification's <c>requestId</c>.
        /// Requires a session manager and that the notification payload contains
        /// a valid session — otherwise it is silently ignored.
        /// </summary>
        private void HandleCancelled(McpRequest request)
        {
            if (_sessions == null)
                return;

            try
            {
                var p = DeserializeParams<McpCancelledParams>(request.Params!);
                if (p?.RequestId == null)
                    return;

                // The session ID must be supplied via the Mcp-Session-Id header on the
                // HTTP request and threaded in through the calling controller.  When
                // no sessionId was set on the McpRequest we cannot route the cancel.
                // Locate all sessions that own this requestId as a best-effort fallback.
                _sessions.CancelRequest(p.SessionId ?? string.Empty, p.RequestId);
                _log.Debug($"notifications/cancelled: requestId={p.RequestId} reason={p.Reason}");
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to process notifications/cancelled: {ex.Message}");
            }
        }

        private McpResponse HandleToolsList(McpRequest request)
        {
            var tools = _registry.GetAllDefinitions();

            // MCP spec supports cursor-based pagination for tools/list.
            // Extract optional cursor from params (may be null for first page).
            string? cursor = null;
            try
            {
                var p = DeserializeParams<System.Collections.Generic.Dictionary<string, object>>(request.Params!);
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

            return McpResponse.Ok(request.Id!, new McpListToolsResult
            {
                Tools      = page,
                NextCursor = nextCursor!
            });
        }

        private async Task<McpResponse> HandleToolsCallAsync(
            McpRequest                       request,
            string?                          sessionId,
            string?                          progressToken,
            Action<McpProgressNotification>? progressCallback,
            CancellationToken                ct)
        {
            // Deserialize the params into McpToolCallParams.
            McpToolCallParams callParams;
            try
            {
                callParams = DeserializeParams<McpToolCallParams>(request.Params!);
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

            if (definition.IsDisabled)
                return McpResponse.Err(request.Id, McpErrorCodes.ReadOnlyMode,
                    $"Tool '{callParams.Name}' is disabled: {definition.DisabledReason}");

            // ── Per-request cancellation ──────────────────────────────────────────
            // Create a linked CTS so the request can be cancelled either by the HTTP
            // context (client disconnect) or by a notifications/cancelled notification.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (sessionId != null && request.Id != null)
                _sessions?.RegisterInFlightRequest(sessionId, request.Id, linkedCts);

            // ── Progress reporting ────────────────────────────────────────────────
            // Install an IProgress<> into the AsyncLocal context so tool handlers can
            // report progress without knowing about SSE or sessions.
            IProgress<McpProgressEvent>? prevProgress = McpProgressContext.Current;
            if (!string.IsNullOrEmpty(progressToken) && progressCallback != null)
            {
                McpProgressContext.Current = new Progress<McpProgressEvent>(evt =>
                {
                    evt.ProgressToken = progressToken!;
                    progressCallback(new McpProgressNotification
                    {
                        Params = new McpProgressNotificationParams
                        {
                            ProgressToken = progressToken!,
                            Progress      = evt.Progress,
                            Total         = evt.Total,
                            Message       = evt.Message,
                        }
                    });
                });
            }

            try
            {
                var result = await _registry.InvokeAsync(
                    callParams.Name,
                    callParams.Arguments ?? new Dictionary<string, object>(),
                    linkedCts.Token).ConfigureAwait(false);

                return McpResponse.Ok(request.Id!, result);
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
                    "The tool call was cancelled.");
            }
            catch (Exception ex)
            {
                _log.Error($"Tool '{callParams.Name}' threw an unhandled exception", ex);
                return McpResponse.Err(request.Id, McpErrorCodes.InternalError,
                    $"Tool execution failed: {ex.Message}");
            }
            finally
            {
                // Restore the previous progress reporter (important for nested dispatches
                // and test isolation).
                McpProgressContext.Current = prevProgress;

                // Unregister the in-flight CTS now that the call has finished.
                if (sessionId != null && request.Id != null)
                    _sessions?.UnregisterInFlightRequest(sessionId, request.Id);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Deserializes a params object (which may be a JObject, Dictionary, or already typed) 
        /// into the target type using JSON round-trip conversion.
        /// </summary>
        private static T DeserializeParams<T>(object raw)
        {
            if (raw == null) return default!;
            if (raw is T typed) return typed;
            if (raw is JObject jobj) return jobj.ToObject<T>()!;
            var json = JsonConvert.SerializeObject(raw);
            return JsonConvert.DeserializeObject<T>(json)!;
        }
    }
}
