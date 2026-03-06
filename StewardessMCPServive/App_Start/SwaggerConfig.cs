using System;
using System.IO;
using System.Web.Http;
using Swashbuckle.Application;
using WebActivatorEx;
using StewardessMCPServive;

[assembly: PreApplicationStartMethod(typeof(StewardessMCPServive.App_Start.SwaggerConfig), "Register")]

namespace StewardessMCPServive.App_Start
{
    /// <summary>
    /// Configures Swashbuckle (OpenAPI/Swagger) for the MCP service.
    /// Automatically invoked during application startup via WebActivatorEx.
    /// </summary>
    public class SwaggerConfig
    {
        public static void Register()
        {
            var httpConfig = GlobalConfiguration.Configuration;

            // Load the XML documentation file for method/property descriptions
            var xmlPath = System.AppDomain.CurrentDomain.BaseDirectory;
            var xmlFile = Path.Combine(xmlPath, "bin", "StewardessMCPServive.xml");

            httpConfig
                .EnableSwagger(c =>
                {
                    c.SingleApiVersion("2.0.0", "StewardessMCPServive")
                        .Description("A production-quality C# .NET Framework 4.7.2 MCP (Model Context Protocol) service " +
                                   "that exposes a local source-code repository to AI agents through a secure Web API " +
                                   "and an MCP-compatible JSON-RPC 2.0 tool surface.");

                    c.IncludeXmlComments(xmlFile);
                    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

                    // Custom operation filter to group endpoints by resource
                    c.GroupActionsBy(apiDesc => ExtractResourceName(apiDesc.ActionDescriptor.ControllerDescriptor.ControllerName));

                    // Add security scheme for API key
                    c.ApiKey("X-Api-Key")
                        .Description("API key authentication (X-Api-Key header or Authorization: Bearer <key>)")
                        .Name("X-Api-Key")
                        .In("header");

                    c.OperationFilter<AddAuthorizationHeaderParameterOperationFilter>();
                })
                .EnableSwaggerUi(c =>
                {
                    c.InjectStylesheet(GetType().Assembly, "StewardessMCPServive.Assets.swagger-ui.css");
                    c.DocumentTitle("StewardessMCPServive API");
                    c.DocExpansion(DocExpansion.List);
                    c.EnableDiscoveryUrlSelector();
                    c.ShowJsonEditor();
                });
        }

        private static string ExtractResourceName(string controllerName)
        {
            // Clean up controller name to resource name
            // HealthController -> Health
            // CapabilitiesController -> Capabilities
            // FileController -> File Operations
            var resourceName = controllerName.Replace("Controller", "");
            
            return resourceName switch
            {
                "Health" => "Health & Diagnostics",
                "Capabilities" => "Health & Diagnostics",
                "Repository" => "Repository Navigation",
                "File" => "File Operations",
                "Search" => "Search Operations",
                "Edit" => "Edit & Write Operations",
                "Git" => "Git Operations",
                "Command" => "Command Execution",
                "Mcp" => "MCP Endpoint",
                _ => resourceName
            };
        }
    }

    /// <summary>
    /// Operation filter to add Authorization header to endpoints that support API key auth.
    /// </summary>
    public class AddAuthorizationHeaderParameterOperationFilter : IOperationFilter
    {
        public void Apply(Operation operation, SchemaRegistry schemaRegistry, System.Web.Http.Description.ApiDescription apiDescription)
        {
            // All endpoints support optional API key auth
            if (operation.parameters == null)
                operation.parameters = new System.Collections.Generic.List<Parameter>();

            operation.parameters.Add(new Parameter
            {
                name = "Authorization",
                description = "Bearer token or API key (optional if X-Api-Key header not set)",
                @default = null,
                required = false,
                type = "string",
                @in = "header"
            });
        }
    }
}
