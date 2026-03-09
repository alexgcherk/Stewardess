// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Web;
using StewardessMCPService.CodeIndexing.Eligibility;
using StewardessMCPService.CodeIndexing.Indexing;
using StewardessMCPService.CodeIndexing.LanguageDetection;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using StewardessMCPService.CodeIndexing.Parsers.Python;
using StewardessMCPService.CodeIndexing.Projection;
using StewardessMCPService.CodeIndexing.Query;
using StewardessMCPService.CodeIndexing.Snapshots;
using StewardessMCPService.CodeIndexing.Source;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Mcp;
using StewardessMCPService.Services;
using StewardessMCPService.Parsers.CSharp;
using System;
using System.IO;
using System.Reflection;

// ── 1. Logging — initialise NLog before anything else ───────────────────────
LoggingBootstrap.EnsureConfigured();
var startupLog = McpLogger.For<Program>();
startupLog.Info("StewardessMCPService.Core starting up...");

// ── 2. Build application ─────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// Wire NLog as the ASP.NET Core logger
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// ── 3. Services — registered as factory lambdas so they are instantiated
//    after builder.Build() when the fully-configured IConfiguration is available.
//    This allows WebApplicationFactory test overrides to take effect.
builder.Services.AddSingleton<McpServiceSettings>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var s = new McpServiceSettings(cfg);
    McpServiceSettings.SetInstance(s);
    return s;
});

builder.Services.AddSingleton<PathValidator>(sp =>
    new PathValidator(sp.GetRequiredService<McpServiceSettings>()));

builder.Services.AddSingleton<ISecurityService>(sp =>
    new SecurityService(
        sp.GetRequiredService<McpServiceSettings>(),
        sp.GetRequiredService<PathValidator>()));

builder.Services.AddSingleton<IAuditService>(sp =>
    new AuditService(sp.GetRequiredService<McpServiceSettings>()));

builder.Services.AddSingleton<IFileSystemService>(sp =>
    new FileSystemService(
        sp.GetRequiredService<McpServiceSettings>(),
        sp.GetRequiredService<PathValidator>(),
        sp.GetRequiredService<IAuditService>()));

builder.Services.AddSingleton<ISearchService>(sp =>
    new SearchService(
        sp.GetRequiredService<McpServiceSettings>(),
        sp.GetRequiredService<PathValidator>()));

builder.Services.AddSingleton<IEditService>(sp =>
    new EditService(
        sp.GetRequiredService<McpServiceSettings>(),
        sp.GetRequiredService<PathValidator>(),
        sp.GetRequiredService<ISecurityService>(),
        sp.GetRequiredService<IAuditService>()));

builder.Services.AddSingleton<IGitService>(sp =>
    new GitService(
        sp.GetRequiredService<McpServiceSettings>(),
        sp.GetRequiredService<PathValidator>()));

builder.Services.AddSingleton<ICommandService>(sp =>
    new CommandService(
        sp.GetRequiredService<McpServiceSettings>(),
        sp.GetRequiredService<PathValidator>(),
        sp.GetRequiredService<IAuditService>()));

builder.Services.AddSingleton<IProjectDetectionService>(sp =>
    new ProjectDetectionService(
        sp.GetRequiredService<McpServiceSettings>(),
        sp.GetRequiredService<PathValidator>()));

// ── Code Indexing services ────────────────────────────────────────────────────
builder.Services.AddSingleton<ILanguageDetector>(_ => new DefaultLanguageDetector());
builder.Services.AddSingleton<IEligibilityPolicy>(_ => new DefaultEligibilityPolicy());
builder.Services.AddSingleton<ISourceProvider>(_ => new FileSystemSourceProvider());
builder.Services.AddSingleton<ISnapshotStore>(_ => new InMemorySnapshotStore());

builder.Services.AddSingleton<IEnumerable<IParserAdapter>>(sp => new IParserAdapter[]
{
    new CSharpParserAdapter(),
    new PythonParserAdapter(),
});

builder.Services.AddSingleton<IEnumerable<ISymbolProjector>>(sp => new ISymbolProjector[]
{
    new CSharpSymbolProjector(),
    new PythonSymbolProjector(),
});

builder.Services.AddSingleton<IIndexingEngine>(sp =>
    new IndexingEngine(
        sp.GetRequiredService<ISourceProvider>(),
        sp.GetRequiredService<IEligibilityPolicy>(),
        sp.GetRequiredService<ILanguageDetector>(),
        sp.GetRequiredService<IEnumerable<IParserAdapter>>(),
        sp.GetRequiredService<ISnapshotStore>(),
        sp.GetRequiredService<IEnumerable<ISymbolProjector>>()));

builder.Services.AddSingleton<IIndexQueryService>(sp =>
    new InMemoryIndexQueryService(sp.GetRequiredService<ISnapshotStore>()));

builder.Services.AddSingleton<McpToolRegistry>(sp =>
    new McpToolRegistry(
        sp.GetRequiredService<McpServiceSettings>(),
        sp.GetRequiredService<IFileSystemService>(),
        sp.GetRequiredService<ISearchService>(),
        sp.GetRequiredService<IEditService>(),
        sp.GetRequiredService<IGitService>(),
        sp.GetRequiredService<ICommandService>(),
        sp.GetRequiredService<IIndexingEngine>(),
        sp.GetRequiredService<IIndexQueryService>()));

// Register IMcpSessionManager (Streamable HTTP transport, MCP 2025-03-26).
builder.Services.AddSingleton<IMcpSessionManager, McpSessionManager>();

builder.Services.AddSingleton<McpToolHandler>(sp =>
    new McpToolHandler(
        sp.GetRequiredService<McpToolRegistry>(),
        sp.GetRequiredService<McpServiceSettings>().ServiceVersion ?? "2.0.0",
        sp.GetRequiredService<IMcpSessionManager>()));

// ── 4. MVC / Controllers ──────────────────────────────────────────────────────
builder.Services
    .AddControllers(o =>
    {
        o.Filters.Add<ApiKeyAuthAttribute>();
        // Suppress .NET 8 behaviour that treats non-nullable `string` properties as
        // [Required], restoring the permissive validation of the original project.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddNewtonsoftJson(o =>
    {
        o.SerializerSettings.ContractResolver     = new CamelCasePropertyNamesContractResolver();
        o.SerializerSettings.NullValueHandling    = NullValueHandling.Ignore;
        o.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
        o.SerializerSettings.Formatting           = Formatting.None;
    });

// ── 6. Swagger / OpenAPI 3.0 ────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "StewardessMCPService — Local Repository MCP API",
        Version     = "v1",
        Description = "MCP-compatible HTTP API that exposes a local source-code repository to AI agents."
    });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type        = SecuritySchemeType.ApiKey,
        In          = ParameterLocation.Header,
        Name        = "X-API-Key",
        Description = "API key for authentication"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });

    // Generate operationId from "{Controller}_{Action}" — required by Open WebUI tool discovery.
    c.CustomOperationIds(apiDesc =>
    {
        var controller = apiDesc.ActionDescriptor.RouteValues["controller"];
        var action     = apiDesc.ActionDescriptor.RouteValues["action"];
        return string.IsNullOrEmpty(controller) ? null : $"{controller}_{action}";
    });

    var xmlFile = Path.ChangeExtension(Assembly.GetExecutingAssembly().GetName().Name ?? "StewardessMCPService.Core", ".xml");
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

// ── 7. Build ──────────────────────────────────────────────────────────────────
var app = builder.Build();

// Resolve singletons so lazy DI factories run and McpServiceSettings.Instance is set.
// Also populate ServiceLocator for legacy code paths (e.g. HealthController.GetDetailedHealth).
{
    var sp = app.Services;
    ServiceLocator.Reset();
    ServiceLocator.RegisterSingleton<McpServiceSettings>(sp.GetRequiredService<McpServiceSettings>());
    ServiceLocator.RegisterSingleton<PathValidator>(sp.GetRequiredService<PathValidator>());
    ServiceLocator.RegisterSingleton<ISecurityService>(sp.GetRequiredService<ISecurityService>());
    ServiceLocator.RegisterSingleton<IAuditService>(sp.GetRequiredService<IAuditService>());
    ServiceLocator.RegisterSingleton<IFileSystemService>(sp.GetRequiredService<IFileSystemService>());
    ServiceLocator.RegisterSingleton<ISearchService>(sp.GetRequiredService<ISearchService>());
    ServiceLocator.RegisterSingleton<IEditService>(sp.GetRequiredService<IEditService>());
    ServiceLocator.RegisterSingleton<IGitService>(sp.GetRequiredService<IGitService>());
    ServiceLocator.RegisterSingleton<ICommandService>(sp.GetRequiredService<ICommandService>());
    ServiceLocator.RegisterSingleton<IProjectDetectionService>(sp.GetRequiredService<IProjectDetectionService>());
    ServiceLocator.RegisterSingleton<McpToolRegistry>(sp.GetRequiredService<McpToolRegistry>());
    ServiceLocator.RegisterSingleton<McpToolHandler>(sp.GetRequiredService<McpToolHandler>());
    ServiceLocator.RegisterSingleton<IMcpSessionManager>(sp.GetRequiredService<IMcpSessionManager>());
}

var settings = app.Services.GetRequiredService<McpServiceSettings>();
startupLog.Info($"RepositoryRoot : {settings.RepositoryRoot}");
startupLog.Info($"ReadOnlyMode   : {settings.ReadOnlyMode}");
startupLog.Info($"RequireApiKey  : {settings.RequireApiKey}");

// ── 8. Middleware pipeline ───────────────────────────────────────────────────
#if DEBUG
// Log every request/response to stdout in DEBUG builds only.
app.UseMiddleware<RequestResponseLoggingMiddleware>();
#endif
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestIdMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "StewardessMCPService v1");
    c.RoutePrefix  = "swagger";
    c.DocumentTitle = "StewardessMCPService API";
});

app.MapControllers();

startupLog.Info("StewardessMCPService.Core started.");

app.Run();
