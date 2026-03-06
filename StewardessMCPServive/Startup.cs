using System.Web.Http;
using Microsoft.Owin;
using StewardessMCPServive.App_Start;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Mcp;
using StewardessMCPServive.Services;
using Owin;

[assembly: OwinStartup(typeof(StewardessMCPServive.Startup))]

namespace StewardessMCPServive
{
    /// <summary>
    /// OWIN startup class.  Wires the DI container, configures logging, and
    /// registers Web API middleware in the OWIN pipeline.
    ///
    /// Invoked automatically by <c>Microsoft.Owin.Host.SystemWeb</c> when the
    /// application starts under IIS / IIS Express.  For self-hosting scenarios
    /// use <c>WebApp.Start&lt;Startup&gt;(url)</c>.
    /// </summary>
    public class Startup
    {
        /// <summary>Configures the OWIN pipeline, dependency injection, and Web API.</summary>
        public void Configuration(IAppBuilder app)
        {
            // ── 1. Logging ───────────────────────────────────────────────────────
            LoggingBootstrap.EnsureConfigured();
            var startupLog = McpLogger.For<Startup>();
            startupLog.Info("StewardessMCPServive starting up...");

            // ── 2. Settings (validates repository root early) ────────────────────
            var settings = McpServiceSettings.Instance;
            startupLog.Info($"RepositoryRoot : {settings.RepositoryRoot}");
            startupLog.Info($"ReadOnlyMode   : {settings.ReadOnlyMode}");
            startupLog.Info($"RequireApiKey  : {settings.RequireApiKey}");

            // ── 3. Dependency container ──────────────────────────────────────────
            RegisterServices(settings);

            // ── 4. Web API ───────────────────────────────────────────────────────
            var httpConfig = new HttpConfiguration();
            WebApiConfig.Register(httpConfig);
            app.UseWebApi(httpConfig);

            startupLog.Info("StewardessMCPServive started.");
        }

        // ── Private: service registrations ──────────────────────────────────────

        private static void RegisterServices(McpServiceSettings settings)
        {
            var pathValidator = new PathValidator(settings);

            // Core services
            var securityService  = new SecurityService(settings, pathValidator);
            var auditService     = new AuditService(settings);
            var fileService      = new FileSystemService(settings, pathValidator, auditService);
            var searchService    = new SearchService(settings, pathValidator);
            var editService      = new EditService(settings, pathValidator, securityService, auditService);
            var gitService       = new GitService(settings, pathValidator);
            var commandService   = new CommandService(settings, pathValidator, auditService);
            var projectService   = new ProjectDetectionService(settings, pathValidator);

            // Register with service locator
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

            // MCP layer
            var registry = new McpToolRegistry(settings, fileService, searchService, editService, gitService, commandService);
            var handler  = new McpToolHandler(registry, settings.ServiceVersion ?? "1.0.0");
            ServiceLocator.RegisterSingleton<McpToolRegistry>(registry);
            ServiceLocator.RegisterSingleton<McpToolHandler>(handler);
        }
    }
}
