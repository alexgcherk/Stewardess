using Microsoft.AspNetCore.Http;
using StewardessMCPService.Models;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace StewardessMCPService.Infrastructure
{
    /// <summary>
    /// ASP.NET Core middleware that catches any unhandled exception and returns
    /// a structured <see cref="ApiResponse{T}"/> JSON error rather than the default
    /// developer exception page.
    /// </summary>
    public sealed class GlobalExceptionMiddleware
    {
        private static readonly McpLogger _log = McpLogger.For<GlobalExceptionMiddleware>();
        private readonly RequestDelegate _next;

        public GlobalExceptionMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            var requestId = context.GetRequestId();
            _log.Error($"Unhandled exception on {context.Request.Method} {context.Request.Path}", ex);

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
                    InnerMessage = IsDebugMode() ? ex.Message : null!
                },
                requestId);

            context.Response.StatusCode  = (int)status;
            context.Response.ContentType = "application/json";

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            await context.Response.WriteAsync(json);
        }

        private static bool IsDebugMode()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
