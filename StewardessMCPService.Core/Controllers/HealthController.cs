using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;

namespace StewardessMCPService.Controllers
{
    /// <summary>
    /// Liveness / readiness endpoints.
    ///
    /// GET /api/health   — basic liveness (always anonymous)
    /// GET /api/health/detailed — extended health with config summary
    /// GET /api/version  — service version
    /// </summary>
    [Route("api/health")]
    public sealed class HealthController : BaseController
    {
        /// <summary>Basic liveness probe. Always returns 200 OK.  No authentication required.</summary>
        [HttpGet, Route(""), AllowAnonymous]
        public IActionResult GetHealth()
        {
            return Ok(new
            {
                status        = "healthy",
                serviceVersion = McpServiceSettings.Instance.ServiceVersion,
                timestamp     = DateTimeOffset.UtcNow
            });
        }

        /// <summary>Extended health including configuration summary.</summary>
        [HttpGet, Route("detailed")]
        public IActionResult GetDetailedHealth()
        {
            var settings = McpServiceSettings.Instance;

            bool repoExists;
            try { repoExists = System.IO.Directory.Exists(settings.RepositoryRoot); }
            catch { repoExists = false; }

            return Ok(new
            {
                status              = repoExists ? "healthy" : "degraded",
                serviceVersion       = settings.ServiceVersion,
                timestamp           = DateTimeOffset.UtcNow,
                repositoryRoot      = settings.RepositoryRoot,
                repositoryAccessible = repoExists,
                readOnlyMode        = settings.ReadOnlyMode,
                apiKeyRequired      = settings.RequireApiKey,
                ipAllowlistActive   = settings.AllowedIPs.Count > 0,
                registeredServices  = ServiceLocator.GetRegisteredTypeNames()
            });
        }

        /// <summary>Returns the service version.</summary>
        [HttpGet, Route("/api/version"), AllowAnonymous]
        public IActionResult GetVersion() =>
            Ok(new { version = McpServiceSettings.Instance.ServiceVersion });
    }
}
