// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Newtonsoft.Json;

namespace StewardessMCPService.Models;
// ────────────────────────────────────────────────────────────────────────────
//  MCP 2025-03-26 — Streamable HTTP Transport
//
//  The 2025-03-26 revision of the MCP specification replaces the older HTTP+SSE
//  transport with a single "Streamable HTTP" endpoint that unifies request/response
//  and server-initiated notifications on two HTTP verbs:
//
//    POST /mcp  — Send a JSON-RPC request or notification.
//                 The server responds with either:
//                   application/json        — single JSON-RPC response (simple calls)
//                   text/event-stream (SSE) — streamed events including progress
//                                             notifications followed by the final
//                                             JSON-RPC response.
//                 The client signals its preference via the Accept header.
//
//    GET  /mcp  — Open a long-lived SSE channel on which the server pushes
//                 server-initiated notifications (tools/list_changed, etc.).
//                 Requires a valid Mcp-Session-Id header to identify the session.
//
//    DELETE /mcp — Terminate a session explicitly.
//
//  Session management
//  ------------------
//  When a client sends initialize, the server creates a session and returns its
//  ID in the Mcp-Session-Id response header.  The client SHOULD include this
//  header on all subsequent requests so the server can route notifications to
//  the right SSE channel.
//
//  Progress notifications
//  ----------------------
//  A client that wants progress updates for a long-running tool call includes a
//  progressToken in the request's _meta object:
//
//    { "method": "tools/call",
//      "params": { "_meta": { "progressToken": "abc123" }, "name": "...", ... } }
//
//  The server emits notifications/progress SSE events during execution:
//
//    { "method": "notifications/progress",
//      "params": { "progressToken": "abc123", "progress": 42, "total": 100,
//                  "message": "Indexing files..." } }
//
//  Request cancellation
//  --------------------
//  A client may cancel an in-flight request by sending:
//
//    { "method": "notifications/cancelled",
//      "params": { "requestId": "123", "reason": "User cancelled" } }
//
//  The server cancels the pending operation via the associated CancellationToken.
// ────────────────────────────────────────────────────────────────────────────

// ── Session ──────────────────────────────────────────────────────────────────

/// <summary>
///     Represents an active MCP client session created during the initialize handshake.
///     Each session has an isolated SSE channel for server-initiated notifications and a
///     registry of in-flight request cancellation tokens.
/// </summary>
public sealed class McpSessionInfo
{
    /// <summary>Globally unique session identifier (UUID v4).</summary>
    public string Id { get; init; } = null!;

    /// <summary>UTC time when this session was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Protocol version negotiated during initialize.
    ///     Stored so the server can tailor responses to client capabilities.
    /// </summary>
    public string ProtocolVersion { get; init; } = null!;

    /// <summary>Optional display name of the connecting client (from ClientInfo).</summary>
    public string? ClientName { get; init; }
}

// ── _meta — request metadata carried inside params ────────────────────────────

/// <summary>
///     Optional metadata block the client may attach to any JSON-RPC request's
///     <c>params</c> object.  Currently used to carry a progress token so the server
///     knows where to route progress notifications.
/// </summary>
public sealed class McpRequestMeta
{
    /// <summary>
    ///     Opaque token chosen by the client.  The server echoes this token back in
    ///     every <c>notifications/progress</c> event so the client can correlate
    ///     progress with the originating request.
    /// </summary>
    public string? ProgressToken { get; set; }
}

// ── Progress ──────────────────────────────────────────────────────────────────

/// <summary>
///     Represents a single progress report emitted by a long-running tool.
///     Sent to the client as a <c>notifications/progress</c> SSE event.
/// </summary>
public sealed class McpProgressEvent
{
    /// <summary>
    ///     Opaque token supplied by the client in <c>_meta.progressToken</c>.
    ///     The server echoes it verbatim so the client can correlate the event
    ///     with the originating tool call.
    /// </summary>
    public string ProgressToken { get; set; } = null!;

    /// <summary>
    ///     Current progress value.  When <see cref="Total" /> is provided this
    ///     value is between 0 and <see cref="Total" />; otherwise it is a
    ///     non-decreasing counter.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    ///     Optional total value that bounds <see cref="Progress" />.
    ///     When present the client can display a percentage or progress bar.
    ///     Null means the total is not known.
    /// </summary>
    public double? Total { get; set; }

    /// <summary>
    ///     Optional human-readable status message, e.g. "Parsing 42 of 100 files".
    ///     May be surfaced by the AI client in its UI.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
///     Full JSON-RPC notification payload for <c>notifications/progress</c>.
///     Serialized and sent as an SSE <c>message</c> event during streaming responses.
/// </summary>
public sealed class McpProgressNotification
{
    /// <summary>JSON-RPC version; always "2.0".</summary>
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>Method name; always "notifications/progress".</summary>
    public string Method { get; set; } = "notifications/progress";

    /// <summary>Progress event data.</summary>
    public McpProgressNotificationParams Params { get; set; } = null!;
}

/// <summary>Params block for a <c>notifications/progress</c> notification.</summary>
public sealed class McpProgressNotificationParams
{
    /// <summary>The progress token the client supplied in <c>_meta.progressToken</c>.</summary>
    public string ProgressToken { get; set; } = null!;

    /// <summary>Current progress value (0 → <see cref="Total" /> when known).</summary>
    public double Progress { get; set; }

    /// <summary>Optional total value. Null when the total is not known.</summary>
    public double? Total { get; set; }

    /// <summary>Optional human-readable message for display.</summary>
    public string? Message { get; set; }
}

// ── Cancellation ─────────────────────────────────────────────────────────────

/// <summary>
///     Params block for a <c>notifications/cancelled</c> notification.
///     A client sends this to ask the server to abort an in-flight request.
/// </summary>
public sealed class McpCancelledParams
{
    /// <summary>
    ///     The <c>id</c> of the JSON-RPC request the client wishes to cancel.
    ///     The server will attempt to abort the associated tool call by signalling
    ///     its <see cref="System.Threading.CancellationToken" />.
    /// </summary>
    public object RequestId { get; set; } = null!;

    /// <summary>Optional human-readable reason for the cancellation.</summary>
    public string? Reason { get; set; }

    /// <summary>
    ///     Optional session ID hint.  When provided by the controller (extracted from
    ///     the <c>Mcp-Session-Id</c> request header) the handler can scope the
    ///     cancellation lookup to a specific session instead of broadcasting.
    ///     This is not part of the MCP spec payload; it is populated server-side only.
    /// </summary>
    [JsonIgnore]
    public string? SessionId { get; set; }
}

// ── SSE wire format helpers ───────────────────────────────────────────────────

/// <summary>
///     Represents a single Server-Sent Event to be written to an SSE response stream.
///     SSE wire format:
///     <code>
///   event: {EventType}\n
///   data: {Data}\n
///   \n
/// </code>
/// </summary>
public sealed class McpSseEvent
{
    /// <summary>
    ///     SSE event type field.  The MCP Streamable HTTP transport uses <c>"message"</c>
    ///     for all JSON-RPC responses and notifications, and <c>"endpoint"</c> for the
    ///     initial handshake on a GET /mcp channel.
    /// </summary>
    public string EventType { get; set; } = "message";

    /// <summary>Raw JSON string to place in the SSE <c>data:</c> field.</summary>
    public string Data { get; set; } = null!;

    /// <summary>
    ///     Optional SSE <c>id:</c> field.  When provided, the client can use this to
    ///     resume an interrupted event stream via the <c>Last-Event-ID</c> header.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    ///     Formats this event into the SSE wire representation.
    ///     Each field is terminated by a single newline; the event is terminated
    ///     by a blank line (double newline after the last field).
    /// </summary>
    public string ToWireFormat()
    {
        var sb = new StringBuilder();
        if (Id != null)
            sb.Append("id: ").Append(Id).Append('\n');
        sb.Append("event: ").Append(EventType).Append('\n');
        sb.Append("data: ").Append(Data).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }
}

// ── Extended tools/call params (includes _meta) ───────────────────────────────

/// <summary>
///     Extended version of <see cref="McpToolCallParams" /> that also accepts the
///     optional <c>_meta</c> block from MCP 2025-03-26.  The <c>_meta</c> field
///     carries the progress token for streaming responses.
/// </summary>
public sealed class McpToolCallParamsWithMeta
{
    /// <summary>Name of the tool to invoke.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Tool arguments as a key-value dictionary.</summary>
    public Dictionary<string, object> Arguments { get; set; } = new();

    /// <summary>
    ///     Optional metadata supplied by the client.  When present and
    ///     <see cref="McpRequestMeta.ProgressToken" /> is set, the server streams
    ///     progress notifications back to the client.
    /// </summary>
    public McpRequestMeta? Meta { get; set; }
}