// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace StewardessMCPService.Infrastructure;

/// <summary>
///     Middleware that logs every HTTP API request and response body to stdout.
///     Only active in DEBUG builds; compiled out entirely in RELEASE.
///     Swagger/OpenAPI metadata and static asset paths are excluded.
/// </summary>
public sealed class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestResponseLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
#if DEBUG
        if (IsMetadataPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        await LogRequest(context);

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            await LogResponse(context, buffer);
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;
        }
#else
            await _next(context);
#endif
    }

#if DEBUG
    /// <summary>
    ///     Returns true for Swagger UI, OpenAPI spec, and other non-API infrastructure paths
    ///     that should not be logged.
    /// </summary>
    private static bool IsMetadataPath(PathString path)
    {
        return path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)
               || path.StartsWithSegments("/favicon.ico", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task LogRequest(HttpContext context)
    {
        var req = context.Request;
        req.EnableBuffering();

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("┌─── REQUEST ─────────────────────────────────────────────────────────────");
        sb.AppendLine($"│  {req.Method} {req.Scheme}://{req.Host}{req.Path}{req.QueryString}");
        sb.AppendLine($"│  {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}");

        foreach (var (key, value) in req.Headers)
            sb.AppendLine($"│  {key}: {value}");

        if (req.ContentLength > 0 || req.Headers.ContainsKey("Transfer-Encoding"))
        {
            req.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            req.Body.Seek(0, SeekOrigin.Begin);
            if (!string.IsNullOrWhiteSpace(body))
            {
                sb.AppendLine("│");
                sb.AppendLine($"│  {body}");
            }
        }

        sb.Append("└─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine(sb.ToString());
    }

    private static async Task LogResponse(HttpContext context, MemoryStream buffer)
    {
        buffer.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        var resp = context.Response;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("┌─── RESPONSE ────────────────────────────────────────────────────────────");
        sb.AppendLine($"│  HTTP {resp.StatusCode}  {context.Request.Method} {context.Request.Path}");
        sb.AppendLine($"│  {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}");

        foreach (var (key, value) in resp.Headers)
            sb.AppendLine($"│  {key}: {value}");

        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.AppendLine("│");
            sb.AppendLine($"│  {body}");
        }

        sb.Append("└─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine(sb.ToString());
    }
#endif
}