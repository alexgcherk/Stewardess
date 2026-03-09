// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Mcp;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using StewardessMCPService.Tests.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StewardessMCPService.Tests.Mcp
{
    /// <summary>
    /// Tests for <see cref="McpToolHandler"/> — MCP spec compliance.
    /// Covers the initialize lifecycle, notification handling, tools/list pagination,
    /// and tools/call dispatch.
    /// </summary>
    public sealed class McpToolHandlerTests : IDisposable
    {
        private readonly TempRepository  _repo;
        private readonly McpToolHandler  _handler;
        private readonly McpToolRegistry _registry;

        public McpToolHandlerTests()
        {
            _repo = new TempRepository();
            _repo.CreateSampleCsStructure();

            var settings   = McpServiceSettings.CreateForTesting(_repo.Root);
            var validator  = new PathValidator(settings);
            var audit      = new AuditService(settings);
            var security   = new SecurityService(settings, validator);

            var fileSvc    = new FileSystemService(settings, validator, audit);
            var searchSvc  = new SearchService(settings, validator);
            var editSvc    = new EditService(settings, validator, security, audit);
            var gitSvc     = new GitService(settings, validator);
            var cmdSvc     = new CommandService(settings, validator, audit);

            _registry = new McpToolRegistry(settings, fileSvc, searchSvc, editSvc, gitSvc, cmdSvc);
            _handler  = new McpToolHandler(_registry, "1.0.0");
        }

        public void Dispose() => _repo.Dispose();

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts an object to JObject using the same camelCase settings as the production
        /// JSON formatter (CamelCasePropertyNamesContractResolver).
        /// </summary>
        private static JObject ToJson(object? obj)
        {
            var settings = new Newtonsoft.Json.JsonSerializerSettings
            {
                ContractResolver  = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
            };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(obj, settings);
            return JObject.Parse(json);
        }

        // ── initialize lifecycle (MCP spec §3.1) ─────────────────────────────────

        [Fact]
        public async Task Initialize_ValidRequest_ReturnsProtocolVersionAndCapabilities()
        {
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = 1,
                Method  = "initialize",
                Params  = JObject.FromObject(new
                {
                    protocolVersion = "2024-11-05",
                    capabilities    = new { },
                    clientInfo      = new { name = "TestClient", version = "0.1" }
                })
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.Null(response.Error);
            Assert.NotNull(response.Result);

            var result = ToJson(response.Result);
            Assert.Equal(McpToolHandler.ProtocolVersion, result["protocolVersion"]?.ToString());
            Assert.NotNull(result["serverInfo"]);
            Assert.NotNull(result["capabilities"]);
            Assert.NotNull(result["capabilities"]?["tools"]);
        }

        [Fact]
        public async Task Initialize_OlderClientVersion_StillSucceeds()
        {
            // The server should accept any client version and respond with its own version.
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = "init-1",
                Method  = "initialize",
                Params  = JObject.FromObject(new { protocolVersion = "2024-01-01" })
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.Null(response.Error);
        }

        // ── Notifications (MCP spec §3.2) ────────────────────────────────────────

        [Fact]
        public async Task Notification_InitializedWithoutId_ReturnsNull()
        {
            // "initialized" is sent as a notification (no id). The server MUST NOT respond.
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = null!,
                Method  = "initialized"
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.Null(response);  // null = no response should be sent
        }

        [Theory]
        [InlineData("notifications/initialized")]
        [InlineData("notifications/cancelled")]
        [InlineData("notifications/tools/list_changed")]
        [InlineData("notifications/progress")]
        public async Task Notification_AnyNotificationsMethod_ReturnsNull(string method)
        {
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = null!,
                Method  = method
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.Null(response);
        }

        [Fact]
        public async Task NotificationWithId_IsRoutedNormally_NotSuppressed()
        {
            // A notifications/* method WITH an id is technically a request, not a notification.
            // It should return MethodNotFound, not be silently consumed.
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = 99,
                Method  = "notifications/cancelled"
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.NotNull(response);
            Assert.NotNull(response.Error);
            Assert.Equal(McpErrorCodes.MethodNotFound, response.Error.Code);
        }

        // ── tools/list (MCP spec §5.4) ────────────────────────────────────────────

        [Fact]
        public async Task ToolsList_NoCursor_ReturnsFirstPage()
        {
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = 2,
                Method  = "tools/list"
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);

            Assert.Null(response.Error);
            var result = ToJson(response.Result);
            var tools  = result["tools"] as JArray;
            Assert.NotNull(tools);
            Assert.NotEmpty(tools);
        }

        [Fact]
        public async Task ToolsList_Pagination_NextCursorPresentWhenMorePages()
        {
            var allTools = _registry.GetAllDefinitions();

            var request = new McpRequest { JsonRpc = "2.0", Id = 3, Method = "tools/list" };
            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            var result = ToJson(response.Result);

            if (allTools.Count > 50)
            {
                Assert.NotNull(result["nextCursor"]);
            }
            else
            {
                // All tools fit on one page — nextCursor must be null/absent.
                Assert.True(result["nextCursor"] == null || result["nextCursor"]?.Type == JTokenType.Null);
            }
        }

        [Fact]
        public async Task ToolsList_EachTool_HasRequiredFields()
        {
            var request = new McpRequest { JsonRpc = "2.0", Id = 4, Method = "tools/list" };
            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            var result = ToJson(response.Result);
            var tools  = (JArray?)result["tools"];
            Assert.NotNull(tools);

            foreach (var tool in tools!)
            {
                var name        = tool["name"]?.ToString();
                var description = tool["description"]?.ToString();
                var schema      = tool["inputSchema"];

                Assert.False(string.IsNullOrEmpty(name),        $"Tool is missing 'name'");
                Assert.False(string.IsNullOrEmpty(description), $"Tool '{name}' is missing 'description'");
                Assert.NotNull(schema);
                Assert.Equal("object", schema["type"]?.ToString());
            }
        }

        // ── tools/call (MCP spec §5.5) ────────────────────────────────────────────

        [Fact]
        public async Task ToolsCall_UnknownTool_ReturnsToolNotFound()
        {
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = 5,
                Method  = "tools/call",
                Params  = JObject.FromObject(new { name = "does_not_exist", arguments = new { } })
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.NotNull(response.Error);
            Assert.Equal(McpErrorCodes.ToolNotFound, response.Error.Code);
        }

        [Fact]
        public async Task ToolsCall_KnownTool_ReturnsContentArray()
        {
            // "get_repository_info" is a safe read-only tool that needs no arguments.
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = 6,
                Method  = "tools/call",
                Params  = JObject.FromObject(new { name = "get_repository_info", arguments = new { } })
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.Null(response.Error);

            var result  = ToJson(response.Result);
            var content = result["content"] as JArray;
            Assert.NotNull(content);
            Assert.NotEmpty(content);
            Assert.Equal("text", content[0]["type"]?.ToString());
        }

        [Fact]
        public async Task ToolsCall_MissingName_ReturnsInvalidParams()
        {
            var request = new McpRequest
            {
                JsonRpc = "2.0",
                Id      = 7,
                Method  = "tools/call",
                Params  = JObject.FromObject(new { arguments = new { } })  // name missing
            };

            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.NotNull(response.Error);
            Assert.Equal(McpErrorCodes.InvalidParams, response.Error.Code);
        }

        // ── ping ──────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Ping_ReturnsOkStatus()
        {
            var request = new McpRequest { JsonRpc = "2.0", Id = 8, Method = "ping" };
            var response = await _handler.DispatchAsync(request, CancellationToken.None);

            Assert.Null(response.Error);
            var result = ToJson(response.Result);
            Assert.Equal("ok", result["status"]?.ToString());
        }

        // ── JSON-RPC envelope validation ─────────────────────────────────────────

        [Fact]
        public async Task Dispatch_WrongJsonRpcVersion_ReturnsInvalidRequest()
        {
            var request = new McpRequest { JsonRpc = "1.0", Id = 9, Method = "ping" };
            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.Equal(McpErrorCodes.InvalidRequest, response.Error!.Code);
        }

        [Fact]
        public async Task Dispatch_UnknownMethod_ReturnsMethodNotFound()
        {
            var request = new McpRequest { JsonRpc = "2.0", Id = 10, Method = "nonexistent/method" };
            var response = await _handler.DispatchAsync(request, CancellationToken.None);
            Assert.Equal(McpErrorCodes.MethodNotFound, response.Error!.Code);
        }

        [Fact]
        public async Task Dispatch_NullRequest_ReturnsError()
        {
            var response = await _handler.DispatchAsync(null!, CancellationToken.None);
            Assert.NotNull(response.Error);
        }
    }
}
