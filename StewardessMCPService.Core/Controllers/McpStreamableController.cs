// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Mcp;
using StewardessMCPService.Models;

namespace StewardessMCPService.Controllers
{
    /// <summary>
    /// MCP 2025-03-26 Streamable HTTP transport endpoint at <c>/mcp</c>.
    ///
    /// Endpoints
    /// ─────────
    ///   POST   /mcp   — dispatch a JSON-RPC request; responds with JSON or SSE stream
    ///   GET    /mcp   — open a server-sent-events channel for server→client notifications
    ///   DELETE /mcp   — terminate a session (client-initiated clean-up)
    ///
    /// Protocol notes (MCP 2025-03-26)
    /// ────────────────────────────────
    /// • On <c>initialize</c> the server creates a session and returns its ID in the
    ///   <c>Mcp-Session-Id</c> response header.
    /// • Clients MUST echo the session ID on all subsequent requests in the same header.
    /// • POST supports two response modes:
    ///     – <c>Accept: application/json</c> (default) — single synchronous JSON response.
    ///     – <c>Accept: text/event-stream</c>           — SSE stream; server emits progress
    ///       notifications and then the final result as individual SSE events.
    /// • GET opens a dedicated SSE channel.  The server pushes asynchronous
    ///   notifications (e.g. <c>notifications/tools/list_changed</c>) through this channel.
    ///   Clients maintain the connection until they terminate the session with DELETE.
    /// • <c>notifications/cancelled</c> (POST, no id) instructs the server to abort a
    ///   specific in-flight request via its registered <see cref="CancellationTokenSource"/>.
    ///
    /// Authentication
    /// ──────────────
    /// POST and DELETE require the standard API-key header (applied globally by
    /// <see cref="ApiKeyAuthFilter"/>).  GET uses <c>[AllowAnonymous]</c> because SSE
    /// clients that have established a session cannot re-set headers on the keep-alive
    /// connection; instead the controller validates the session ID manually.
    /// </summary>
    [Route("mcp")]
    public sealed class McpStreamableController : BaseController
    {
        private const string SessionHeader   = "Mcp-Session-Id";
        private const string SseContentType  = "text/event-stream";
        private const string JsonContentType = "application/json";

        // SSE keep-alive interval; prevents intermediary proxies from closing idle connections.
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerSettings SseJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        private McpToolHandler        Handler   => GetService<McpToolHandler>();
        private IMcpSessionManager    Sessions  => GetService<IMcpSessionManager>();
        private static readonly McpLogger _log  = McpLogger.For<McpStreamableController>();

        // ────────────────────────────────────────────────────────────────────────────
        // POST /mcp
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Main JSON-RPC dispatch.  Accepts a single request or notification.
        ///
        /// If the client sends <c>Accept: text/event-stream</c> the response is an
        /// SSE stream with one or more progress events followed by the final result.
        /// Otherwise the response is a single <c>application/json</c> body.
        ///
        /// The <c>Mcp-Session-Id</c> header is returned on <c>initialize</c> responses
        /// and must be echoed on all subsequent requests.
        /// </summary>
        [HttpPost, Route("")]
        public async Task<IActionResult> Post(
            [FromBody] JObject? body,
            CancellationToken   ct)
        {
            if (body == null)
            {
                return Content(
                    SerializeRpcError(null, McpErrorCodes.ParseError, "Request body is missing or not valid JSON."),
                    JsonContentType, Encoding.UTF8);
            }

            McpRequest request;
            try
            {
                request = body.ToObject<McpRequest>()!;
            }
            catch (Exception ex)
            {
                return Content(
                    SerializeRpcError(null, McpErrorCodes.ParseError, $"Failed to parse request: {ex.Message}"),
                    JsonContentType, Encoding.UTF8);
            }

            // Extract session ID from header (may be absent on first request / initialize).
            var sessionId = Request.Headers[SessionHeader].ToString();
            if (string.IsNullOrWhiteSpace(sessionId)) sessionId = null;

            // Extract progress token from _meta (only relevant for tools/call).
            string? progressToken = ExtractProgressToken(request);

            // ── SSE streaming mode ────────────────────────────────────────────────
            bool wantsStream = (Request.GetTypedHeaders().Accept?.Count > 0) &&
                               Request.Headers["Accept"].ToString().Contains(SseContentType, StringComparison.OrdinalIgnoreCase);

            if (wantsStream && !string.IsNullOrEmpty(progressToken))
            {
                return await DispatchSseAsync(request, sessionId, progressToken, ct)
                    .ConfigureAwait(false);
            }

            // ── Standard JSON response ────────────────────────────────────────────
            var response = await Handler.DispatchAsync(
                request, sessionId, progressToken, ct)
                .ConfigureAwait(false);

            // Notifications (no id): return 202 Accepted with no body.
            if (response == null)
                return StatusCode(202);

            // On initialize, attach the session ID header if one was created.
            AttachSessionHeader(response);

            return Content(SerializeResponse(response), JsonContentType, Encoding.UTF8);
        }

        // ────────────────────────────────────────────────────────────────────────────
        // GET /mcp  — SSE notification channel
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens a long-lived server-sent-events channel for server→client notifications.
        ///
        /// The client MUST supply a valid session ID via the <c>Mcp-Session-Id</c> header.
        /// The server writes SSE keep-alive comments every 30 seconds and pushes
        /// <c>notifications/*</c> events as they occur.  The stream ends when the session
        /// is terminated (via <c>DELETE /mcp</c>) or the client disconnects.
        ///
        /// Note: this action is marked [AllowAnonymous] because SSE clients cannot easily
        /// re-set headers after the initial connection; session ID validation is done manually.
        /// </summary>
        [HttpGet, Route(""), AllowAnonymous]
        public async Task GetNotifications(CancellationToken ct)
        {
            var sessionId = Request.Headers[SessionHeader].ToString();

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("Missing Mcp-Session-Id header.", ct).ConfigureAwait(false);
                return;
            }

            var channel = Sessions.GetNotificationChannel(sessionId);
            if (channel == null)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync($"Session '{sessionId}' not found.", ct).ConfigureAwait(false);
                return;
            }

            Response.ContentType = SseContentType;
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no"; // Disable Nginx buffering

            // Send the initial "endpoint" event so client knows the channel is open.
            await WriteSseEventAsync(
                Response.Body,
                new McpSseEvent { EventType = "endpoint", Data = "" },
                ct).ConfigureAwait(false);

            using var keepAliveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var keepAliveTask = Task.Run(async () =>
            {
                while (!keepAliveCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(KeepAliveInterval, keepAliveCts.Token).ConfigureAwait(false);
                    // SSE comment line (no event type/data) keeps the connection alive.
                    try
                    {
                        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(": keep-alive\n\n"), keepAliveCts.Token)
                            .ConfigureAwait(false);
                        await Response.Body.FlushAsync(keepAliveCts.Token).ConfigureAwait(false);
                    }
                    catch { break; }
                }
            }, keepAliveCts.Token);

            try
            {
                await foreach (var evt in channel.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await WriteSseEventAsync(Response.Body, evt, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* client disconnected or session terminated */ }
            finally
            {
                await keepAliveCts.CancelAsync().ConfigureAwait(false);
                try { await keepAliveTask.ConfigureAwait(false); } catch { }
            }
        }

        // ────────────────────────────────────────────────────────────────────────────
        // DELETE /mcp  — terminate session
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Terminates the session identified by the <c>Mcp-Session-Id</c> header.
        /// Cancels all in-flight requests belonging to the session and closes the
        /// notification SSE channel.
        /// </summary>
        [HttpDelete, Route("")]
        public IActionResult Delete()
        {
            var sessionId = Request.Headers[SessionHeader].ToString();

            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest("MISSING_SESSION", "Missing Mcp-Session-Id header.");

            if (Sessions.TryGetSession(sessionId) == null)
                return StatusCode(404);

            Sessions.TerminateSession(sessionId);
            _log.Info($"Session terminated via DELETE /mcp: {sessionId}");
            return StatusCode(200);
        }

        // ────────────────────────────────────────────────────────────────────────────
        // Private helpers
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Dispatches a request in SSE mode.  Progress notifications are sent as
        /// individual SSE events and the final JSON-RPC result is the last event.
        /// </summary>
        private async Task<IActionResult> DispatchSseAsync(
            McpRequest        request,
            string?           sessionId,
            string?           progressToken,
            CancellationToken ct)
        {
            Response.ContentType = SseContentType;
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            // Callback invoked by the handler for each progress event.
            async void OnProgress(McpProgressNotification notification)
            {
                try
                {
                    var sseEvent = new McpSseEvent
                    {
                        EventType = "message",
                        Data      = JsonConvert.SerializeObject(notification, SseJsonSettings),
                    };
                    await WriteSseEventAsync(Response.Body, sseEvent, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch { /* stream may have closed */ }
            }

            var response = await Handler.DispatchAsync(
                request, sessionId, progressToken, ct, OnProgress)
                .ConfigureAwait(false);

            // Notifications (no id): stream ends with no result event.
            if (response == null)
                return new EmptyResult();

            AttachSessionHeader(response);

            // Write the final JSON-RPC response as the last SSE event.
            var finalEvent = new McpSseEvent
            {
                EventType = "message",
                Data      = SerializeResponse(response),
            };
            await WriteSseEventAsync(Response.Body, finalEvent, ct).ConfigureAwait(false);

            return new EmptyResult();
        }

        /// <summary>
        /// If the response result is an <see cref="McpInitializeResult"/> with a session ID
        /// embedded by the handler, write it to the <c>Mcp-Session-Id</c> response header.
        /// </summary>
        private void AttachSessionHeader(McpResponse response)
        {
            if (response?.Result is McpInitializeResult initResult &&
                !string.IsNullOrEmpty(initResult.SessionId))
            {
                Response.Headers[SessionHeader] = initResult.SessionId;
            }
        }

        /// <summary>Extracts <c>_meta.progressToken</c> from a <c>tools/call</c> request params.</summary>
        private static string? ExtractProgressToken(McpRequest request)
        {
            if (!string.Equals(request.Method, "tools/call", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                if (request.Params is JObject p)
                {
                    var meta  = p["_meta"] as JObject;
                    var token = meta?["progressToken"]?.ToString();
                    return string.IsNullOrEmpty(token) ? null : token;
                }
            }
            catch { /* tolerate */ }

            return null;
        }

        /// <summary>
        /// Writes a single Server-Sent Event to the given stream.
        /// Format:
        /// <code>
        ///   event: {EventType}\n
        ///   data: {Data}\n
        ///   \n
        /// </code>
        /// </summary>
        private static async Task WriteSseEventAsync(
            Stream            stream,
            McpSseEvent       evt,
            CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(evt.ToWireFormat());
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Serialises a <see cref="McpResponse"/> to JSON using the standard settings.</summary>
        private static string SerializeResponse(McpResponse response) =>
            JsonConvert.SerializeObject(response, SseJsonSettings);

        /// <summary>Serialises a bare JSON-RPC error response (used before handler is invoked).</summary>
        private static string SerializeRpcError(object? id, int code, string message) =>
            SerializeResponse(McpResponse.Err(id!, code, message));
    }
}
