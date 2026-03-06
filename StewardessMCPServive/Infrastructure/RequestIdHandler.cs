using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;

namespace StewardessMCPServive.Infrastructure
{
    /// <summary>
    /// HTTP message handler that ensures every request carries a unique correlation ID.
    ///
    /// Resolution order:
    ///   1. Use the value of the incoming <c>X-Request-Id</c> header (if valid).
    ///   2. Fall back to a new <see cref="Guid"/>.
    ///
    /// The resolved ID is stored in <c>request.Properties["MCP_RequestId"]</c> and
    /// echoed back in the response as <c>X-Request-Id</c>.
    /// </summary>
    public sealed class RequestIdHandler : DelegatingHandler
    {
        /// <summary>Request-properties key used to store and retrieve the correlation ID.</summary>
        public const string RequestIdKey    = "MCP_RequestId";
        /// <summary>HTTP header name used to read and echo the correlation ID.</summary>
        public const string RequestIdHeader = "X-Request-Id";

        /// <summary>Resolves or generates a correlation ID and attaches it to the request and response.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestId = ResolveRequestId(request);
            request.Properties[RequestIdKey] = requestId;

            var response = await base.SendAsync(request, cancellationToken);

            if (response != null)
                response.Headers.TryAddWithoutValidation(RequestIdHeader, requestId);

            return response;
        }

        private static string ResolveRequestId(HttpRequestMessage request)
        {
            if (request.Headers.TryGetValues(RequestIdHeader, out var vals))
            {
                var incoming = System.Linq.Enumerable.FirstOrDefault(vals);
                if (!string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 128)
                    return incoming;
            }

            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Convenience extension methods for extracting the request ID from an
    /// <see cref="HttpRequestMessage"/>.
    /// </summary>
    public static class RequestIdExtensions
    {
        /// <summary>Returns the correlation ID attached by <see cref="RequestIdHandler"/>.</summary>
        public static string GetRequestId(this HttpRequestMessage request)
        {
            if (request?.Properties.TryGetValue(RequestIdHandler.RequestIdKey, out var val) == true)
                return val as string ?? string.Empty;

            return string.Empty;
        }
    }
}
