// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using StewardessMCPService.Configuration;
using StewardessMCPService.Models;

namespace StewardessMCPService.Infrastructure;

/// <summary>
///     ASP.NET Core authorization filter that enforces API key authentication.
///     When <see cref="McpServiceSettings.RequireApiKey" /> is true the filter
///     checks for the key in the following locations (in order):
///     1. <c>X-API-Key</c> request header
///     2. <c>Authorization: Bearer &lt;key&gt;</c> header
///     Apply globally in Program.cs or mark individual actions with <c>[AllowAnonymous]</c> to bypass.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiKeyAuthAttribute : Attribute, IAsyncAuthorizationFilter
{
    private static readonly McpLogger _log = McpLogger.For<ApiKeyAuthAttribute>();

    /// <summary>Validates the API key on every incoming HTTP request.</summary>
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var settings = McpServiceSettings.Instance;
        if (!settings.RequireApiKey) return Task.CompletedTask;
        if (SkipAuth(context)) return Task.CompletedTask;

        var httpContext = context.HttpContext;
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // IP allowlist check
        if (settings.AllowedIPs.Count > 0)
            if (!settings.AllowedIPs.Contains(clientIp, StringComparer.OrdinalIgnoreCase))
            {
                _log.Warn($"IP {clientIp} is not in the AllowedIPs list — rejected.");
                Reject(context, HttpStatusCode.Forbidden,
                    ErrorCodes.Forbidden, "Your IP address is not permitted to access this service.");
                return Task.CompletedTask;
            }

        var suppliedKey = ExtractKey(httpContext.Request);
        if (string.IsNullOrWhiteSpace(suppliedKey))
        {
            _log.Warn($"Missing API key from {clientIp}");
            Reject(context, HttpStatusCode.Unauthorized,
                ErrorCodes.Unauthorized, "An API key is required. Supply it via the X-API-Key header.");
            return Task.CompletedTask;
        }

        if (!ConstantTimeEquals(settings.ApiKey, suppliedKey))
        {
            _log.Warn($"Invalid API key from {clientIp}");
            Reject(context, HttpStatusCode.Unauthorized,
                ErrorCodes.Unauthorized, "The supplied API key is invalid.");
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool SkipAuth(AuthorizationFilterContext ctx)
    {
        // Check endpoint-level metadata
        var endpoint = ctx.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null) return true;

        // Check action descriptor attributes
        if (ctx.ActionDescriptor is ControllerActionDescriptor cad)
        {
            if (cad.MethodInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any()) return true;
            if (cad.ControllerTypeInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any()) return true;
        }

        return false;
    }

    private static string? ExtractKey(HttpRequest request)
    {
        // 1. X-API-Key header
        if (request.Headers.TryGetValue("X-API-Key", out var vals))
            return vals.FirstOrDefault();

        // 2. Authorization: Bearer <key>
        if (request.Headers.TryGetValue("Authorization", out var auth))
        {
            var value = auth.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return value.Substring("Bearer ".Length).Trim();
        }

        return null;
    }

    private static void Reject(AuthorizationFilterContext ctx, HttpStatusCode status, string code, string message)
    {
        var payload = ApiResponse<object>.Fail(new ApiError { Code = code, Message = message });
        ctx.Result = new ObjectResult(payload) { StatusCode = (int)status };
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a == null || b == null) return false;
        var diff = a.Length ^ b.Length;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}