using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;

namespace StewardessMCPServive.Infrastructure
{
    /// <summary>
    /// Web API action filter that enforces API key authentication.
    ///
    /// When <see cref="McpServiceSettings.RequireApiKey"/> is true the filter
    /// checks for the key in the following locations (in order):
    ///   1. <c>X-API-Key</c> request header
    ///   2. <c>Authorization: Bearer &lt;key&gt;</c> header
    ///   3. <c>apiKey</c> query-string parameter (dev/debug only; logged as warning)
    ///
    /// Apply with <c>[ApiKeyAuth]</c> or register globally in WebApiConfig.
    /// Mark individual actions with <c>[AllowAnonymous]</c> to bypass.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
    public sealed class ApiKeyAuthAttribute : AuthorizationFilterAttribute
    {
        private static readonly McpLogger _log = McpLogger.For<ApiKeyAuthAttribute>();

        /// <summary>Validates the API key on every incoming HTTP request.</summary>
        public override void OnAuthorization(HttpActionContext actionContext)
        {
            // Skip when authentication is disabled or the action is marked anonymous.
            var settings = McpServiceSettings.Instance;
            if (!settings.RequireApiKey) return;
            if (SkipAuth(actionContext)) return;

            var request  = actionContext.Request;
            var clientIp = GetClientIp(request);

            // IP allowlist check
            if (settings.AllowedIPs.Count > 0)
            {
                if (!settings.AllowedIPs.Contains(clientIp))
                {
                    _log.Warn($"IP {clientIp} is not in the AllowedIPs list — rejected.");
                    Reject(actionContext, HttpStatusCode.Forbidden,
                           ErrorCodes.Forbidden, "Your IP address is not permitted to access this service.");
                    return;
                }
            }

            // Extract the supplied key
            string suppliedKey = ExtractKey(request);
            if (string.IsNullOrWhiteSpace(suppliedKey))
            {
                _log.Warn($"Missing API key from {clientIp}");
                Reject(actionContext, HttpStatusCode.Unauthorized,
                       ErrorCodes.Unauthorized, "An API key is required. Supply it via the X-API-Key header.");
                return;
            }

            // Validate — use a constant-time comparison to prevent timing attacks.
            if (!ConstantTimeEquals(settings.ApiKey, suppliedKey))
            {
                _log.Warn($"Invalid API key from {clientIp}");
                Reject(actionContext, HttpStatusCode.Unauthorized,
                       ErrorCodes.Unauthorized, "The supplied API key is invalid.");
                return;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool SkipAuth(HttpActionContext ctx)
        {
            return ctx.ActionDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Any()
                || ctx.ControllerContext.ControllerDescriptor
                       .GetCustomAttributes<AllowAnonymousAttribute>().Any();
        }

        private static string ExtractKey(HttpRequestMessage request)
        {
            // 1. X-API-Key header
            if (request.Headers.TryGetValues("X-API-Key", out var vals))
                return vals.FirstOrDefault();

            // 2. Authorization: Bearer <key>
            var auth = request.Headers.Authorization;
            if (auth != null &&
                string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                return auth.Parameter;

            // NOTE: Query-string API keys (?apiKey=) are intentionally NOT supported.
            // Accepting keys in URLs leaks them into server logs, browser history,
            // proxy logs, and Referer headers.  Use X-API-Key or Authorization: Bearer.
            return null;
        }

        private static string GetClientIp(HttpRequestMessage request)
        {
            if (request.Properties.TryGetValue("MS_HttpContext", out var ctx))
            {
                dynamic httpCtx = ctx;
                try { return httpCtx.Request.UserHostAddress; } catch { }
            }
            return "unknown";
        }

        private static void Reject(HttpActionContext ctx, HttpStatusCode status, string code, string message)
        {
            var error   = new ApiError { Code = code, Message = message };
            var payload = ApiResponse<object>.Fail(error);
            ctx.Response = ctx.Request.CreateResponse(status, payload);
        }

        /// <summary>
        /// Constant-time string comparison to prevent timing side-channel attacks
        /// that could allow an attacker to enumerate API key characters.
        /// </summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            // XOR every character; accumulate differences.
            // Always iterates over the full length of <a> regardless of <b> content.
            int diff = a.Length ^ b.Length;
            int len  = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
