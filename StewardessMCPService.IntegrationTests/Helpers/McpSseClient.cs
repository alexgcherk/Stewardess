// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StewardessMCPService.IntegrationTests.Helpers
{
    /// <summary>
    /// Minimal Server-Sent Events (SSE) client for integration tests.
    ///
    /// Connects to a <c>text/event-stream</c> endpoint and yields parsed
    /// <see cref="SseEvent"/> records.  Designed to work with both:
    ///   • <c>GET /mcp</c>  — long-lived notification channel.
    ///   • <c>POST /mcp</c> with <c>Accept: text/event-stream</c> — streaming tool call.
    /// </summary>
    public sealed class McpSseClient
    {
        private readonly HttpClient _http;

        /// <summary>Creates an SSE client backed by the given <see cref="HttpClient"/>.</summary>
        public McpSseClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        // ── GET /mcp ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens a <c>GET /mcp</c> SSE channel and yields events until the stream ends
        /// or <paramref name="ct"/> is cancelled.
        ///
        /// The caller is responsible for cancelling <paramref name="ct"/> to close the
        /// connection; the stream is otherwise infinite.
        /// </summary>
        /// <param name="sessionId">The session ID to send in the <c>Mcp-Session-Id</c> header.</param>
        /// <param name="ct">Cancellation token — cancel to close the SSE connection.</param>
        public IAsyncEnumerable<SseEvent> ReadNotificationsAsync(
            string            sessionId,
            CancellationToken ct = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "mcp");
            request.Headers.Add("Mcp-Session-Id", sessionId);
            request.Headers.Add("Accept", "text/event-stream");

            return ReadSseStreamAsync(request, ct);
        }

        // ── POST /mcp with SSE response ──────────────────────────────────────────

        /// <summary>
        /// Posts a JSON-RPC request to <c>POST /mcp</c> with <c>Accept: text/event-stream</c>
        /// and yields all SSE events (progress notifications + the final result).
        ///
        /// Each event's <see cref="SseEvent.Data"/> contains a JSON-RPC 2.0 object.
        /// The last event is the final tool response.
        /// </summary>
        /// <param name="requestBody">JSON-RPC 2.0 request body as a <see cref="JObject"/>.</param>
        /// <param name="sessionId">Optional session ID for the <c>Mcp-Session-Id</c> header.</param>
        /// <param name="apiKey">Optional API key for the <c>Authorization: Bearer</c> header.</param>
        /// <param name="ct">Cancellation token.</param>
        public IAsyncEnumerable<SseEvent> PostStreamAsync(
            JObject           requestBody,
            string?           sessionId = null,
            string?           apiKey    = null,
            CancellationToken ct        = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Accept", "text/event-stream");

            if (!string.IsNullOrEmpty(sessionId))
                request.Headers.Add("Mcp-Session-Id", sessionId);

            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("Authorization", $"Bearer {apiKey}");

            return ReadSseStreamAsync(request, ct);
        }

        // ── Core SSE reader ──────────────────────────────────────────────────────

        private async IAsyncEnumerable<SseEvent> ReadSseStreamAsync(
            HttpRequestMessage                             request,
            [EnumeratorCancellation] CancellationToken    ct)
        {
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct)
                .ConfigureAwait(false);

            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? eventType = null;
            var     dataLines = new List<string>();

            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                if (line == null)
                    yield break; // stream ended

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventType = line.Substring("event:".Length).Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    dataLines.Add(line.Substring("data:".Length).TrimStart());
                }
                else if (line.Length == 0 && dataLines.Count > 0)
                {
                    // Blank line = end of event.
                    yield return new SseEvent
                    {
                        EventType = eventType ?? "message",
                        Data      = string.Join("\n", dataLines),
                    };

                    eventType = null;
                    dataLines.Clear();
                }
                // Lines starting with ':' are comments (keep-alive) — silently ignored.
            }
        }
    }

    /// <summary>A single parsed Server-Sent Event.</summary>
    public sealed class SseEvent
    {
        /// <summary>The SSE <c>event:</c> field value (defaults to "message").</summary>
        public string EventType { get; init; } = "message";

        /// <summary>The SSE <c>data:</c> field value.</summary>
        public string Data { get; init; } = null!;

        /// <summary>Parses <see cref="Data"/> as a JSON object.</summary>
        public JObject ParseJson() => JObject.Parse(Data);
    }
}
