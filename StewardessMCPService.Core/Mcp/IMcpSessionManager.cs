// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using StewardessMCPService.Models;

namespace StewardessMCPService.Mcp
{
    /// <summary>
    /// Manages MCP client sessions for the Streamable HTTP transport (2025-03-26).
    ///
    /// Each session is created during the <c>initialize</c> handshake and is identified
    /// by an opaque UUID that the server returns in the <c>Mcp-Session-Id</c> response
    /// header.  Clients include this ID on subsequent requests so the server can:
    ///
    /// <list type="bullet">
    ///   <item>Route server-initiated notifications to the correct SSE channel.</item>
    ///   <item>Cancel in-flight tool calls when a <c>notifications/cancelled</c>
    ///         notification arrives.</item>
    /// </list>
    ///
    /// Sessions without an active GET /mcp SSE listener still function correctly;
    /// server-initiated notifications are simply queued and discarded when the queue
    /// is full (the bounded channel drops oldest messages).
    /// </summary>
    public interface IMcpSessionManager
    {
        /// <summary>
        /// Creates a new session and returns its metadata.
        /// Called when the server processes an <c>initialize</c> request.
        /// </summary>
        /// <param name="protocolVersion">Protocol version negotiated with the client.</param>
        /// <param name="clientName">Optional display name of the connecting client.</param>
        McpSessionInfo CreateSession(string protocolVersion, string? clientName = null);

        /// <summary>
        /// Looks up a session by its ID.  Returns <c>null</c> when the session does not
        /// exist or has been terminated.
        /// </summary>
        McpSessionInfo? TryGetSession(string sessionId);

        /// <summary>
        /// Terminates a session and releases all associated resources (SSE channel,
        /// in-flight cancellation tokens).
        /// </summary>
        void TerminateSession(string sessionId);

        // ── In-flight request cancellation ────────────────────────────────────────

        /// <summary>
        /// Registers a <see cref="CancellationTokenSource"/> for an in-flight request
        /// so it can be cancelled later by <see cref="CancelRequest"/>.
        ///
        /// The source is automatically unregistered when the tool call completes.
        /// </summary>
        /// <param name="sessionId">Session that owns the request.</param>
        /// <param name="requestId">The JSON-RPC request <c>id</c> (number or string).</param>
        /// <param name="cts">The <see cref="CancellationTokenSource"/> for the operation.</param>
        void RegisterInFlightRequest(string sessionId, object requestId, CancellationTokenSource cts);

        /// <summary>
        /// Cancels and unregisters the in-flight request identified by
        /// <paramref name="requestId"/> within the given session.
        ///
        /// A no-op when the request has already completed or the session does not exist.
        /// </summary>
        /// <param name="sessionId">Session that owns the request.</param>
        /// <param name="requestId">The JSON-RPC request <c>id</c> to cancel.</param>
        void CancelRequest(string sessionId, object requestId);

        /// <summary>
        /// Unregisters a completed in-flight request.
        /// Must be called after every tool call (success or failure) to free the
        /// associated <see cref="CancellationTokenSource"/>.
        /// </summary>
        void UnregisterInFlightRequest(string sessionId, object requestId);

        // ── SSE notification channel ──────────────────────────────────────────────

        /// <summary>
        /// Publishes a server-initiated SSE notification to the session's outbound channel.
        /// The notification will be delivered to any active GET /mcp SSE listener.
        ///
        /// The message is dropped silently if the channel is full (bounded capacity)
        /// or the session does not exist.
        /// </summary>
        /// <param name="sessionId">Target session ID.</param>
        /// <param name="sseEvent">The pre-serialized SSE wire message to deliver.</param>
        void PublishNotification(string sessionId, McpSseEvent sseEvent);

        /// <summary>
        /// Returns the reader end of the session's notification channel so that the
        /// GET /mcp controller action can consume queued notifications over SSE.
        /// Returns <c>null</c> when the session does not exist.
        /// </summary>
        ChannelReader<McpSseEvent>? GetNotificationChannel(string sessionId);
    }
}
