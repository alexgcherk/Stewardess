using System;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using Microsoft.Owin.Testing;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Mcp;
using StewardessMCPServive.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Owin;

namespace StewardessMCPServive.IntegrationTests.Helpers
{
    /// <summary>
    /// Bootstraps a complete in-process OWIN test server backed by a temporary
    /// repository directory.  Designed for use as an xUnit
    /// <c>IClassFixture&lt;McpTestServer&gt;</c> — one server instance is shared
    /// across all tests in a class and disposed when the class finishes.
    /// </summary>
    public sealed class McpTestServer : IDisposable
    {
        private readonly TestServer          _server;
        private readonly TempTestRepository  _repo;
        private          bool                _disposed;

        // ── Public surface ───────────────────────────────────────────────────────

        /// <summary>HTTP client wired directly to the in-process OWIN pipeline.</summary>
        public HttpClient HttpClient => _server.HttpClient;

        /// <summary>Absolute path of the temporary repository root.</summary>
        public string RepositoryRoot => _repo.Root;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a temporary repository, registers all services, and starts
        /// the in-process OWIN test server.
        /// </summary>
        public McpTestServer()
        {
            _repo = new TempTestRepository();

            var settings = McpServiceSettings.CreateForTesting(
                repositoryRoot: _repo.Root,
                readOnly:       false,
                allowedCommands: new[]
                {
                    "dotnet build",
                    "dotnet restore",
                    "dotnet test",
                    "dotnet run",
                    "msbuild"
                });

            // ServiceLocator is a static container — reset before wiring so
            // registrations from any previously executed test do not bleed in.
            ServiceLocator.Reset();
            RegisterServices(settings);

            _server = TestServer.Create(app =>
            {
                var config = new HttpConfiguration();
                ConfigureWebApi(config);
                app.UseWebApi(config);
            });
        }

        // ── Service wiring (mirrors Startup.RegisterServices) ────────────────────

        private static void RegisterServices(McpServiceSettings settings)
        {
            var pathValidator   = new PathValidator(settings);
            var securityService = new SecurityService(settings, pathValidator);
            var auditService    = new AuditService(settings);
            var fileService     = new FileSystemService(settings, pathValidator, auditService);
            var searchService   = new SearchService(settings, pathValidator);
            var editService     = new EditService(settings, pathValidator, securityService, auditService);
            var gitService      = new GitService(settings, pathValidator);
            var commandService  = new CommandService(settings, pathValidator, auditService);
            var projectService  = new ProjectDetectionService(settings, pathValidator);

            ServiceLocator.RegisterSingleton<ISecurityService>(securityService);
            ServiceLocator.RegisterSingleton<IAuditService>(auditService);
            ServiceLocator.RegisterSingleton<IFileSystemService>(fileService);
            ServiceLocator.RegisterSingleton<ISearchService>(searchService);
            ServiceLocator.RegisterSingleton<IEditService>(editService);
            ServiceLocator.RegisterSingleton<IGitService>(gitService);
            ServiceLocator.RegisterSingleton<ICommandService>(commandService);
            ServiceLocator.RegisterSingleton<IProjectDetectionService>(projectService);
            ServiceLocator.RegisterSingleton<PathValidator>(pathValidator);
            ServiceLocator.RegisterSingleton<McpServiceSettings>(settings);

            var registry = new McpToolRegistry(settings, fileService, searchService,
                                               editService, gitService, commandService);
            var handler  = new McpToolHandler(registry, settings.ServiceVersion ?? "1.0.0");
            ServiceLocator.RegisterSingleton<McpToolRegistry>(registry);
            ServiceLocator.RegisterSingleton<McpToolHandler>(handler);
        }

        // ── Web API configuration (mirrors WebApiConfig.Register, minus Swagger) ─

        private static void ConfigureWebApi(HttpConfiguration config)
        {
            config.Formatters.JsonFormatter.SerializerSettings = new JsonSerializerSettings
            {
                ContractResolver     = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling    = NullValueHandling.Ignore,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                Formatting           = Formatting.None
            };

            config.Formatters.Remove(config.Formatters.XmlFormatter);

            config.Filters.Add(new ApiKeyAuthAttribute());
            config.MessageHandlers.Add(new RequestIdHandler());
            config.Services.Replace(typeof(IExceptionHandler), new GlobalExceptionHandler());

            config.MapHttpAttributeRoutes();
            config.Routes.MapHttpRoute(
                name:          "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults:      new { id = RouteParameter.Optional });
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _server?.Dispose();
            ServiceLocator.Reset();
            _repo?.Dispose();
        }
    }
}
