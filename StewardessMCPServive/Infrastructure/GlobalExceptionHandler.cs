using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Infrastructure
{
    /// <summary>
    /// Catches any unhandled exception that escapes a controller action and returns
    /// a structured <see cref="ApiResponse{T}"/> JSON error rather than the default
    /// HTML error page.
    ///
    /// Registered via <c>config.Services.Replace(typeof(IExceptionHandler), ...)</c>
    /// in WebApiConfig.
    /// </summary>
    public sealed class GlobalExceptionHandler : ExceptionHandler
    {
        private static readonly McpLogger _log = McpLogger.For<GlobalExceptionHandler>();

        /// <summary>Converts unhandled exceptions to structured JSON error responses.</summary>
        public override void Handle(ExceptionHandlerContext context)
        {
            var ex        = context.Exception;
            var request   = context.Request;
            var requestId = request?.GetRequestId() ?? string.Empty;

            _log.Error($"Unhandled exception on {request?.Method} {request?.RequestUri}", ex);

            string code;
            HttpStatusCode status;

            if (ex is UnauthorizedAccessException)
            {
                code   = ErrorCodes.Unauthorized;
                status = HttpStatusCode.Unauthorized;
            }
            else if (ex is ArgumentException || ex is FormatException)
            {
                code   = ErrorCodes.InvalidRequest;
                status = HttpStatusCode.BadRequest;
            }
            else if (ex is OperationCanceledException)
            {
                code   = ErrorCodes.TimeoutExceeded;
                status = HttpStatusCode.RequestTimeout;
            }
            else if (ex is NotImplementedException)
            {
                code   = ErrorCodes.NotImplemented;
                status = HttpStatusCode.NotImplemented;
            }
            else
            {
                code   = ErrorCodes.InternalError;
                status = HttpStatusCode.InternalServerError;
            }

            var payload = ApiResponse<object>.Fail(
                new ApiError
                {
                    Code         = code,
                    Message      = "An unexpected error occurred.",
                    InnerMessage = IsDebugMode() ? ex.Message : null
                },
                requestId);

            context.Result = new ErrorResult(request, status, payload);
        }

        /// <summary>Always returns true so that every unhandled exception is processed.</summary>
        public override bool ShouldHandle(ExceptionHandlerContext context) => true;

        private static bool IsDebugMode()
        {
#if DEBUG
            return true;
#else
            // In release builds, InnerMessage is suppressed to avoid leaking
            // internal file paths, stack frames, or exception details to callers.
            return false;
#endif
        }

        // ── Inner result ─────────────────────────────────────────────────────────

        private sealed class ErrorResult : IHttpActionResult
        {
            private readonly HttpRequestMessage _request;
            private readonly HttpStatusCode _status;
            private readonly object _payload;

            public ErrorResult(HttpRequestMessage request, HttpStatusCode status, object payload)
            {
                _request = request;
                _status  = status;
                _payload = payload;
            }

            public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
            {
                var response = _request.CreateResponse(_status, _payload);
                return Task.FromResult(response);
            }
        }
    }
}
