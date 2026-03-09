// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Threading.Channels;
using StewardessMCPService.Models;

namespace StewardessMCPService.Mcp;

/// <summary>
///     Thread-safe, in-memory implementation of <see cref="IMcpSessionManager" />.
///     Design notes
///     ─────────────
///     • Sessions are stored in a <see cref="ConcurrentDictionary{TKey,TValue}" /> so
///     that concurrent requests from different clients never block each other.
///     • Each session maintains its own bounded <see cref="Channel{T}" /> (capacity 128)
///     for outbound SSE notifications.  When the channel is full the oldest message is
///     dropped rather than blocking the caller.  This protects the server against a
///     slow or disconnected GET /mcp listener that is not reading its channel.
///     • In-flight request cancellation tokens are stored per-session in a secondary
///     <see cref="ConcurrentDictionary{TKey,TValue}" /> keyed by request id
///     (converted to string for uniform comparison).
/// </summary>
public sealed class McpSessionManager : IMcpSessionManager, IDisposable
{
    private const int SseChannelCapacity = 128;

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>
    ///     Terminates all active sessions and releases all resources.
    ///     Called on application shutdown.
    /// </summary>
    public void Dispose()
    {
        foreach (var key in _sessions.Keys)
            TerminateSession(key);
    }

    // ── IMcpSessionManager ────────────────────────────────────────────────────

    /// <inheritdoc />
    public McpSessionInfo CreateSession(string protocolVersion, string? clientName = null)
    {
        var info = new McpSessionInfo
        {
            Id = Guid.NewGuid().ToString("N"), // compact lowercase hex UUID
            CreatedAt = DateTimeOffset.UtcNow,
            ProtocolVersion = protocolVersion ?? string.Empty,
            ClientName = clientName
        };

        // Bounded channel — if full, the TryWrite below will return false and the
        // notification is silently dropped.  This prevents an idle SSE listener
        // from causing unbounded memory growth.
        var channel = Channel.CreateBounded<McpSseEvent>(new BoundedChannelOptions(SseChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true, // only the GET /mcp handler reads
            SingleWriter = false, // multiple tool calls may write concurrently
            AllowSynchronousContinuations = false
        });

        var entry = new SessionEntry(info, channel);
        _sessions[info.Id] = entry;
        return info;
    }

    /// <inheritdoc />
    public McpSessionInfo? TryGetSession(string sessionId)
    {
        if (sessionId == null) return null;
        return _sessions.TryGetValue(sessionId, out var entry) ? entry.Info : null;
    }

    /// <inheritdoc />
    public void TerminateSession(string sessionId)
    {
        if (sessionId == null) return;
        if (_sessions.TryRemove(sessionId, out var entry))
            entry.Dispose();
    }

    // ── In-flight request cancellation ────────────────────────────────────────

    /// <inheritdoc />
    public void RegisterInFlightRequest(string sessionId, object requestId, CancellationTokenSource cts)
    {
        if (sessionId == null || requestId == null || cts == null) return;
        if (!_sessions.TryGetValue(sessionId, out var entry)) return;
        entry.InFlight[RequestKey(requestId)] = cts;
    }

    /// <inheritdoc />
    public void CancelRequest(string sessionId, object requestId)
    {
        if (sessionId == null || requestId == null) return;
        if (!_sessions.TryGetValue(sessionId, out var entry)) return;

        var key = RequestKey(requestId);
        if (entry.InFlight.TryGetValue(key, out var cts))
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                /* already completed */
            }
    }

    /// <inheritdoc />
    public void UnregisterInFlightRequest(string sessionId, object requestId)
    {
        if (sessionId == null || requestId == null) return;
        if (!_sessions.TryGetValue(sessionId, out var entry)) return;

        var key = RequestKey(requestId);
        if (entry.InFlight.TryRemove(key, out var cts))
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                /* fine */
            }
    }

    // ── SSE notification channel ──────────────────────────────────────────────

    /// <inheritdoc />
    public void PublishNotification(string sessionId, McpSseEvent sseEvent)
    {
        if (sessionId == null || sseEvent == null) return;
        if (!_sessions.TryGetValue(sessionId, out var entry)) return;

        // TryWrite is non-blocking; if the channel is full the event is dropped
        // (BoundedChannelFullMode.DropOldest will drop the oldest to make room).
        entry.Channel.Writer.TryWrite(sseEvent);
    }

    /// <inheritdoc />
    public ChannelReader<McpSseEvent>? GetNotificationChannel(string sessionId)
    {
        if (sessionId == null) return null;
        return _sessions.TryGetValue(sessionId, out var entry)
            ? entry.Channel.Reader
            : null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    ///     Converts a JSON-RPC request id (which may be int, long, string, etc.) to a
    ///     stable string key for the in-flight dictionary.
    /// </summary>
    private static string RequestKey(object id)
    {
        return id?.ToString() ?? string.Empty;
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    /// <summary>Internal per-session state bundle.</summary>
    private sealed class SessionEntry : IDisposable
    {
        public SessionEntry(McpSessionInfo info, Channel<McpSseEvent> channel)
        {
            Info = info;
            Channel = channel;
        }

        public McpSessionInfo Info { get; }

        /// <summary>Bounded outbound SSE channel for this session.</summary>
        public Channel<McpSseEvent> Channel { get; }

        /// <summary>
        ///     In-flight request cancellation tokens keyed by request id string.
        /// </summary>
        public ConcurrentDictionary<string, CancellationTokenSource> InFlight { get; } =
            new(StringComparer.Ordinal);

        public void Dispose()
        {
            // Complete the channel writer so any GET /mcp reader breaks out of its loop.
            Channel.Writer.TryComplete();

            // Cancel and dispose all in-flight requests.
            foreach (var cts in InFlight.Values)
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    /* already disposed */
                }

            InFlight.Clear();
        }
    }
}