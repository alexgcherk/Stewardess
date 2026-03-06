using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;

namespace StewardessMCPServive.Controllers
{
    /// <summary>
    /// Base class for all MCP service controllers.
    /// Provides common helpers: service resolution, response factories, request ID extraction.
    /// </summary>
    public abstract class BaseController : ApiController
    {
        // ── Service resolution ───────────────────────────────────────────────────

        /// <summary>Resolves a registered service from the service locator.</summary>
        protected T GetService<T>() => ServiceLocator.Resolve<T>();

        // ── Request ID ──────────────────────────────────────────────────────────

        /// <summary>Gets the correlation ID for the current request.</summary>
        protected string RequestId => Request.GetRequestId();

        // ── Response factories ───────────────────────────────────────────────────

        /// <summary>Returns a 200 OK response wrapping the given data.</summary>
        protected new HttpResponseMessage Ok<T>(T data)
        {
            var payload = ApiResponse<T>.Ok(data, RequestId);
            return Request.CreateResponse(HttpStatusCode.OK, payload);
        }

        /// <summary>Returns a 201 Created response wrapping the given data.</summary>
        protected HttpResponseMessage Created<T>(T data)
        {
            var payload = ApiResponse<T>.Ok(data, RequestId);
            return Request.CreateResponse(HttpStatusCode.Created, payload);
        }

        /// <summary>Returns a 204 No Content response.</summary>
        protected HttpResponseMessage NoContent()
        {
            return Request.CreateResponse(HttpStatusCode.NoContent);
        }

        /// <summary>Returns an error response with the given HTTP status, error code, and message.</summary>
        protected HttpResponseMessage Fail(HttpStatusCode status, string code, string message)
        {
            var payload = ApiResponse<object>.Fail(code, message, RequestId);
            return Request.CreateResponse(status, payload);
        }

        /// <summary>Returns a 400 Bad Request response.</summary>
        protected HttpResponseMessage BadRequest(string code, string message) =>
            Fail(HttpStatusCode.BadRequest, code, message);

        /// <summary>Returns a 404 Not Found response.</summary>
        protected HttpResponseMessage NotFound(string code, string message) =>
            Fail(HttpStatusCode.NotFound, code, message);

        /// <summary>Returns a 403 Forbidden response.</summary>
        protected HttpResponseMessage Forbidden(string code, string message) =>
            Fail(HttpStatusCode.Forbidden, code, message);

        /// <summary>Returns a 500 Internal Server Error response.</summary>
        protected HttpResponseMessage ServerError(string message) =>
            Fail(HttpStatusCode.InternalServerError, ErrorCodes.InternalError, message);

        // ── Exception → HTTP ─────────────────────────────────────────────────────

        /// <summary>
        /// Maps a caught exception to an appropriate HTTP response.
        /// Keeps controllers free of repetitive try/catch blocks.
        /// </summary>
        protected HttpResponseMessage HandleException(Exception ex)
        {
            switch (ex)
            {
                case ArgumentException ae:
                    return BadRequest(ErrorCodes.InvalidRequest, ae.Message);

                case FileNotFoundException _:
                case DirectoryNotFoundException _:
                    return NotFound(ErrorCodes.PathNotFound, ex.Message);

                case UnauthorizedAccessException _:
                    return Fail(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, ex.Message);

                case OperationCanceledException _:
                    return Fail(HttpStatusCode.RequestTimeout, ErrorCodes.TimeoutExceeded,
                                "The operation timed out or was cancelled.");

                case NotSupportedException _:
                    return Fail(HttpStatusCode.NotImplemented, ErrorCodes.NotImplemented, ex.Message);

                default:
                    McpLogger.For<BaseController>().Error("Unhandled controller exception", ex);
                    return ServerError("An unexpected error occurred.");
            }
        }

        // ── Client IP ────────────────────────────────────────────────────────────

        /// <summary>Gets the remote IP address of the current request caller.</summary>
        protected string ClientIp
        {
            get
            {
                if (Request.Properties.TryGetValue("MS_HttpContext", out var ctx))
                {
                    dynamic httpCtx = ctx;
                    try { return (string)httpCtx.Request.UserHostAddress; } catch { }
                }
                return "unknown";
            }
        }
    }
}
