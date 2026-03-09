using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace StewardessMCPService.Infrastructure
{
    /// <summary>
    /// ASP.NET Core middleware that ensures every request carries a unique correlation ID.
    ///
    /// Resolution order:
    ///   1. Use the value of the incoming <c>X-Request-Id</c> header (if valid).
    ///   2. Fall back to a new <see cref="Guid"/>.
    ///
    /// The resolved ID is stored in <c>HttpContext.Items["MCP_RequestId"]</c> and
    /// echoed back in the response as <c>X-Request-Id</c>.
    /// </summary>
    public sealed class RequestIdMiddleware
    {
        /// <summary>HttpContext.Items key used to store and retrieve the correlation ID.</summary>
        public const string RequestIdKey    = "MCP_RequestId";
        /// <summary>HTTP header name used to read and echo the correlation ID.</summary>
        public const string RequestIdHeader = "X-Request-Id";

        private readonly RequestDelegate _next;

        public RequestIdMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var requestId = ResolveRequestId(context.Request);
            context.Items[RequestIdKey] = requestId;
            context.Response.Headers[RequestIdHeader] = requestId;

            await _next(context);
        }

        private static string ResolveRequestId(HttpRequest request)
        {
            if (request.Headers.TryGetValue(RequestIdHeader, out var vals))
            {
                var incoming = vals.ToString();
                if (!string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 128)
                    return incoming;
            }

            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>Convenience extension for reading the request ID from HttpContext.</summary>
    public static class RequestIdExtensions
    {
        /// <summary>Returns the correlation ID stored by <see cref="RequestIdMiddleware"/>.</summary>
        public static string GetRequestId(this HttpContext context)
        {
            if (context?.Items.TryGetValue(RequestIdMiddleware.RequestIdKey, out var val) == true)
                return val as string ?? string.Empty;
            return string.Empty;
        }
    }
}
