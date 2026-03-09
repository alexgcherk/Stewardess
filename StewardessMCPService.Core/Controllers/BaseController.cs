// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using Microsoft.AspNetCore.Mvc;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using System;
using System.IO;
using System.Net;

namespace StewardessMCPService.Controllers
{
    /// <summary>
    /// Base class for all MCP service controllers.
    /// Provides common helpers: service resolution, response factories, request ID extraction.
    /// </summary>
    [ApiController]
    public abstract class BaseController : ControllerBase
    {
        // ── Service resolution ───────────────────────────────────────────────────

        /// <summary>Resolves a registered service from the DI container via HttpContext.</summary>
        protected T GetService<T>() where T : notnull =>
            (T)HttpContext.RequestServices.GetService(typeof(T))!;

        // ── Request ID ──────────────────────────────────────────────────────────

        /// <summary>Gets the correlation ID for the current request.</summary>
        protected string RequestId => HttpContext.GetRequestId();

        // ── Response factories ───────────────────────────────────────────────────

        /// <summary>Returns a 200 OK response wrapping the given data.</summary>
        protected IActionResult Ok<T>(T data)
        {
            var payload = ApiResponse<T>.Ok(data, RequestId);
            return new ObjectResult(payload) { StatusCode = 200 };
        }

        /// <summary>Returns a 201 Created response wrapping the given data.</summary>
        protected IActionResult Created<T>(T data)
        {
            var payload = ApiResponse<T>.Ok(data, RequestId);
            return new ObjectResult(payload) { StatusCode = 201 };
        }

        /// <summary>Returns a 204 No Content response.</summary>
        protected new IActionResult NoContent() =>
            new StatusCodeResult(204);

        /// <summary>Returns an error response with the given HTTP status, error code, and message.</summary>
        protected IActionResult Fail(HttpStatusCode status, string code, string message)
        {
            var payload = ApiResponse<object>.Fail(code, message, RequestId);
            return new ObjectResult(payload) { StatusCode = (int)status };
        }

        /// <summary>Returns a 400 Bad Request response.</summary>
        protected IActionResult BadRequest(string code, string message) =>
            Fail(HttpStatusCode.BadRequest, code, message);

        /// <summary>Returns a 404 Not Found response.</summary>
        protected IActionResult NotFound(string code, string message) =>
            Fail(HttpStatusCode.NotFound, code, message);

        /// <summary>Returns a 403 Forbidden response.</summary>
        protected IActionResult Forbidden(string code, string message) =>
            Fail(HttpStatusCode.Forbidden, code, message);

        /// <summary>Returns a 500 Internal Server Error response.</summary>
        protected IActionResult ServerError(string message) =>
            Fail(HttpStatusCode.InternalServerError, ErrorCodes.InternalError, message);

        // ── Exception → HTTP ─────────────────────────────────────────────────────

        /// <summary>Maps a caught exception to an appropriate HTTP response.</summary>
        protected IActionResult HandleException(Exception ex)
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
        protected string ClientIp =>
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
