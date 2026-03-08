using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StewardessMCPServive.Configuration;

namespace StewardessMCPServive.IntegrationTests.Helpers
{
    /// <summary>
    /// Bootstraps a complete in-process ASP.NET Core test server backed by a
    /// temporary repository directory using <see cref="WebApplicationFactory{TEntryPoint}"/>.
    /// Designed for use as an xUnit <c>IClassFixture&lt;McpTestServer&gt;</c>.
    /// </summary>
    public sealed class McpTestServer : IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly TempTestRepository             _repo;
        private readonly McpServiceSettings             _settings;
        private          bool                           _disposed;

        // ── Public surface ───────────────────────────────────────────────────────

        /// <summary>HTTP client wired directly to the in-process ASP.NET Core pipeline.</summary>
        public HttpClient HttpClient => _factory.CreateClient();

        /// <summary>Absolute path of the temporary repository root.</summary>
        public string RepositoryRoot => _settings.RepositoryRoot;

        /// <summary>Service settings used by this test server.</summary>
        public McpServiceSettings Settings => _settings;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a temporary repository and starts the in-process test server.
        /// Used by xUnit IClassFixture injection.
        /// </summary>
        public McpTestServer() : this(null, false) { }

        /// <summary>
        /// Creates an in-process test server pointed at <paramref name="repositoryRoot"/>.
        /// When <paramref name="repositoryRoot"/> is null a fresh temp directory is used.
        /// Internal so xUnit only sees the single public parameterless constructor.
        /// </summary>
        internal McpTestServer(string repositoryRoot, bool requireApiKey = false)
        {
            _repo = repositoryRoot == null ? new TempTestRepository() : null;
            var repoRoot = repositoryRoot ?? _repo.Root;
            var apiKey   = requireApiKey ? "test-api-key-12345" : null;

            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["Mcp:RepositoryRoot"]  = repoRoot,
                ["Mcp:ReadOnly"]        = "false",
                ["Mcp:ApiKey"]          = apiKey ?? "",
                ["Mcp:AllowedCommands"] = "dotnet build,dotnet restore,dotnet test,dotnet run,msbuild",
            };

            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                        config.AddInMemoryCollection(inMemoryConfig));
                });

            // Trigger server startup so the DI container is fully initialised.
            _ = _factory.Server;
            _settings = _factory.Services.GetRequiredService<McpServiceSettings>();
        }

        /// <summary>
        /// Creates a typed HTTP client for convenient API calls.
        /// </summary>
        /// <param name="includeApiKey">When true, includes the API key in the Authorization header.</param>
        public McpRestClient CreateHttpClient(bool includeApiKey = true)
        {
            var client = _factory.CreateClient();
            var apiKey = includeApiKey ? _settings.ApiKey : null;
            return new McpRestClient(client, apiKey);
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _factory?.Dispose();
            _repo?.Dispose();
        }
    }
}
