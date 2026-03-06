using System;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using StewardessMCPServive.Infrastructure;
using Swashbuckle.Application;

namespace StewardessMCPServive.App_Start
{
    /// <summary>
    /// Configures ASP.NET Web API 2 routing, formatters, filters, and Swagger/OpenAPI.
    /// Called from <see cref="Startup"/>.
    /// </summary>
    public static class WebApiConfig
    {
        /// <summary>Registers Web API routes, formatters, filters, and Swagger.</summary>
        public static void Register(HttpConfiguration config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            // ── JSON formatter ───────────────────────────────────────────────────
            var jsonFormatter = config.Formatters.JsonFormatter;
            jsonFormatter.SerializerSettings = new JsonSerializerSettings
            {
                ContractResolver     = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling    = NullValueHandling.Ignore,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                Formatting           = Formatting.None
            };

            // Remove XML formatter — JSON only.
            config.Formatters.Remove(config.Formatters.XmlFormatter);

            // ── Global filters ───────────────────────────────────────────────────
            config.Filters.Add(new ApiKeyAuthAttribute());

            // ── Message handlers ────────────────────────────────────────────────
            config.MessageHandlers.Add(new RequestIdHandler());

            // ── Exception handling ──────────────────────────────────────────────
            config.Services.Replace(typeof(IExceptionHandler), new GlobalExceptionHandler());

            // ── Routes ──────────────────────────────────────────────────────────
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name:          "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults:      new { id = RouteParameter.Optional });

            // ── Swagger / OpenAPI ────────────────────────────────────────────────
            config.EnableSwagger(c =>
            {
                c.SingleApiVersion("v1", "StewardessMCPServive — Local Repository MCP API");
                var xmlPath = GetXmlCommentPath();
                if (System.IO.File.Exists(xmlPath))
                    c.IncludeXmlComments(xmlPath);
                c.DescribeAllEnumsAsStrings();
            })
            .EnableSwaggerUi(c =>
            {
                c.DocumentTitle("StewardessMCPServive API");
            });
        }

        private static string GetXmlCommentPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Web app projects output to bin\ directly (no Debug/ subfolder).
            var inBin = System.IO.Path.Combine(baseDir, "bin", "StewardessMCPServive.xml");
            if (System.IO.File.Exists(inBin)) return inBin;
            // Fallback: same directory as the DLL (IIS Express / self-host).
            return System.IO.Path.Combine(baseDir, "StewardessMCPServive.xml");
        }
    }
}
