// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StewardessMCPService.IntegrationTests.Helpers;
using StewardessMCPService.Mcp;
using Xunit;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    /// <summary>
    /// Integration tests for the MCP 2025-03-26 Streamable HTTP transport (<c>POST/GET/DELETE /mcp</c>).
    ///
    /// Covers:
    ///   • POST /mcp — initialize (session creation, Mcp-Session-Id header)
    ///   • POST /mcp — tools/list (JSON response)
    ///   • POST /mcp — tools/call with JSON response
    ///   • POST /mcp — notifications/cancelled (202, no body)
    ///   • DELETE /mcp — session termination
    ///   • DELETE /mcp — missing session header → 400
    ///   • DELETE /mcp — unknown session → 404
    ///   • GET /mcp — missing session header → 400
    ///   • GET /mcp — unknown session → 404
    ///   • Protocol version in initialize response is 2025-03-26
    /// </summary>
    [Collection(IntegrationTestCollection.Name)]
    public sealed class McpStreamableHttpTests : IDisposable
    {
        private readonly McpTestServer  _server;
        private readonly HttpClient     _http;
        private readonly string?        _apiKey;

        public McpStreamableHttpTests()
        {
            var tempRepo = new TempTestRepository();
            _server = new McpTestServer(tempRepo.Root, requireApiKey: false);
            _http   = _server.HttpClient;
            _apiKey = null; // No API key required for this server fixture.
        }

        public void Dispose() => _server.Dispose();

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static JObject Rpc(string method, object? @params = null, int id = 1)
        {
            var obj = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"]      = id,
                ["method"]  = method,
            };
            if (@params != null)
                obj["params"] = JObject.FromObject(@params);
            return obj;
        }

        private static JObject RpcNotification(string method, object? @params = null)
        {
            var obj = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"]  = method,
            };
            if (@params != null)
                obj["params"] = JObject.FromObject(@params);
            return obj;
        }

        private HttpRequestMessage McpPost(JObject body, string? sessionId = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(sessionId))
                request.Headers.Add("Mcp-Session-Id", sessionId);
            if (!string.IsNullOrEmpty(_apiKey))
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            return request;
        }

        private async Task<(JObject Body, string? SessionId)> PostMcpAsync(
            JObject body, string? sessionId = null)
        {
            using var response = await _http.SendAsync(McpPost(body, sessionId));
            var raw = await response.Content.ReadAsStringAsync();
            var retSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
                ? string.Join("", vals) : null;
            return (JObject.Parse(raw), retSessionId);
        }

        // ── initialize ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Post_Initialize_Returns200WithProtocolVersion()
        {
            var (body, _) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo      = new { name = "TestClient", version = "1.0" },
                capabilities    = new { }
            }));

            var result = body["result"] as JObject;
            Assert.NotNull(result);

            var version = result!["protocolVersion"]?.Value<string>();
            Assert.Equal(McpToolHandler.ProtocolVersion, version);
        }

        [Fact]
        public async Task Post_Initialize_ReturnsMcpSessionIdHeader()
        {
            var (_, sessionId) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo      = new { name = "TestClient", version = "1.0" },
                capabilities    = new { }
            }));

            Assert.False(string.IsNullOrWhiteSpace(sessionId),
                "Server must return Mcp-Session-Id header on initialize");
        }

        [Fact]
        public async Task Post_Initialize_SessionIdIsUnique()
        {
            var (_, sessionId1) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo = new { name = "A", version = "1" },
                capabilities = new { }
            }));

            var (_, sessionId2) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo = new { name = "B", version = "1" },
                capabilities = new { }
            }));

            Assert.NotEqual(sessionId1, sessionId2);
        }

        // ── tools/list ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Post_ToolsList_Returns200WithTools()
        {
            // First establish a session.
            var (_, sessionId) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo = new { name = "TestClient", version = "1.0" },
                capabilities = new { }
            }));

            var (body, _) = await PostMcpAsync(Rpc("tools/list"), sessionId);

            var result = body["result"] as JObject;
            Assert.NotNull(result);

            var tools = result!["tools"] as JArray;
            Assert.NotNull(tools);
            Assert.True(tools!.Count > 0, "Server should expose at least one tool");
        }

        // ── tools/call ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Post_ToolsCall_GetRepositoryInfo_ReturnsResult()
        {
            var (_, sessionId) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo = new { name = "TestClient", version = "1.0" },
                capabilities = new { }
            }));

            // Use 'get_repository_info' — a simple read-only tool with no required parameters.
            var (body, _) = await PostMcpAsync(
                Rpc("tools/call", new { name = "get_repository_info", arguments = new { } }),
                sessionId);

            // Result must be present (not an error).
            var result = body["result"] as JObject;
            Assert.NotNull(result);
            var rpcError = body["error"];
            Assert.Null(rpcError);
        }

        [Fact]
        public async Task Post_ToolsCall_UnknownTool_ReturnsError()
        {
            var (_, sessionId) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo = new { name = "TestClient", version = "1.0" },
                capabilities = new { }
            }));

            var (body, _) = await PostMcpAsync(
                Rpc("tools/call", new { name = "does_not_exist", arguments = new { } }),
                sessionId);

            var error = body["error"] as JObject;
            Assert.NotNull(error);
            // ToolNotFound = -32003
            Assert.Equal(-32003, error!["code"]?.Value<int>());
        }

        // ── notifications (no response) ──────────────────────────────────────────

        [Fact]
        public async Task Post_Notification_Returns202NoBody()
        {
            // Notifications have no id — server must return 202 with no body.
            using var request = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent(
                    RpcNotification("notifications/initialized").ToString(),
                    Encoding.UTF8, "application/json"),
            };

            using var response = await _http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(string.IsNullOrWhiteSpace(body), "202 response should have no body");
        }

        [Fact]
        public async Task Post_CancelledNotification_Returns202()
        {
            // notifications/cancelled has no id → 202.
            using var request = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent(
                    RpcNotification("notifications/cancelled",
                        new { requestId = "999", reason = "test" }).ToString(),
                    Encoding.UTF8, "application/json"),
            };

            using var response = await _http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        // ── DELETE /mcp ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Delete_WithValidSession_Returns200()
        {
            // Create a session first.
            var (_, sessionId) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo = new { name = "TestClient", version = "1.0" },
                capabilities = new { }
            }));

            Assert.NotNull(sessionId);

            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "mcp");
            deleteRequest.Headers.Add("Mcp-Session-Id", sessionId!);

            using var response = await _http.SendAsync(deleteRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Delete_MissingSessionHeader_Returns400()
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, "mcp");
            using var response = await _http.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Delete_UnknownSessionId_Returns404()
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, "mcp");
            request.Headers.Add("Mcp-Session-Id", "ghost-session-that-does-not-exist");

            using var response = await _http.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // ── GET /mcp ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Get_MissingSessionHeader_Returns400()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "mcp");
            request.Headers.Add("Accept", "text/event-stream");

            using var response = await _http.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Get_UnknownSessionId_Returns404()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "mcp");
            request.Headers.Add("Accept", "text/event-stream");
            request.Headers.Add("Mcp-Session-Id", "ghost-session");

            using var response = await _http.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Get_ValidSession_StartsStreamAndReceivesEndpointEvent()
        {
            // Create a session.
            var (_, sessionId) = await PostMcpAsync(Rpc("initialize", new
            {
                protocolVersion = "2025-03-26",
                clientInfo = new { name = "TestClient", version = "1.0" },
                capabilities = new { }
            }));

            Assert.NotNull(sessionId);

            // Open SSE channel and read just the first event (the endpoint event),
            // then cancel the stream.
            using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var sseClient    = new McpSseClient(_http);
            var events       = new List<SseEvent>();

            await foreach (var evt in sseClient.ReadNotificationsAsync(sessionId!, cts.Token))
            {
                events.Add(evt);
                // Cancel after receiving the first event.
                await cts.CancelAsync();
            }

            // We should have received the endpoint handshake event.
            Assert.NotEmpty(events);
            Assert.Equal("endpoint", events[0].EventType);
        }

        // ── Backward compatibility: POST /mcp/v1/ still works ───────────────────

        [Fact]
        public async Task LegacyEndpoint_PostMcpV1_StillResponds()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "mcp/v1/")
            {
                Content = new StringContent(
                    Rpc("ping").ToString(),
                    Encoding.UTF8, "application/json"),
            };

            using var response = await _http.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
