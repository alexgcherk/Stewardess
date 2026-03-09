// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;
using Xunit;

namespace StewardessMCPService.Tests.Infrastructure;

/// <summary>
///     Unit tests for <see cref="GlobalExceptionMiddleware" />.
///     Uses <see cref="DefaultHttpContext" /> with a <see cref="MemoryStream" /> response body
///     so no web host is required.
/// </summary>
public sealed class GlobalExceptionMiddlewareTests
{
    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NoException_DelegatesToNext()
    {
        var nextCalled = false;
        var middleware = new GlobalExceptionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = MakeContext();
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        // Default status code — middleware did not override it
        Assert.Equal(200, context.Response.StatusCode);
    }

    // ── Exception → status code mapping ─────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_UnauthorizedAccessException_Returns401()
    {
        var middleware = Throwing(new UnauthorizedAccessException("denied"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains(ErrorCodes.Unauthorized, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_ArgumentException_Returns400()
    {
        var middleware = Throwing(new ArgumentException("bad arg"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains(ErrorCodes.InvalidRequest, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_FormatException_Returns400()
    {
        var middleware = Throwing(new FormatException("bad format"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains(ErrorCodes.InvalidRequest, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceledException_Returns408()
    {
        var middleware = Throwing(new OperationCanceledException("timed out"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.RequestTimeout, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains(ErrorCodes.TimeoutExceeded, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_NotImplementedException_Returns501()
    {
        var middleware = Throwing(new NotImplementedException("todo"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.NotImplemented, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains(ErrorCodes.NotImplemented, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_Returns500()
    {
        var middleware = Throwing(new InvalidOperationException("unexpected"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains(ErrorCodes.InternalError, body, StringComparison.OrdinalIgnoreCase);
    }

    // ── Response format ──────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_AnyException_SetsJsonContentType()
    {
        var middleware = Throwing(new Exception("oops"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Contains("application/json", context.Response.ContentType,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_AnyException_ResponseBodyIsValidJson()
    {
        var middleware = Throwing(new Exception("oops"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        var body = await ReadBodyAsync(context);

        // Should not throw when parsing as JSON
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_AnyException_SuccessIsFalse()
    {
        var middleware = Throwing(new Exception("oops"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        var body = await ReadBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_AnyException_ResponseContainsErrorObject()
    {
        var middleware = Throwing(new Exception("problem"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        var body = await ReadBodyAsync(context);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.True(error.TryGetProperty("code", out _), "error object must have a 'code' field");
    }

    // ── Derived exception types ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ArgumentNullException_Returns400()
    {
        // ArgumentNullException derives from ArgumentException — must still return 400.
        var middleware = Throwing(new ArgumentNullException("param"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_TaskCanceledException_Returns408()
    {
        // TaskCanceledException derives from OperationCanceledException — must return 408.
        var middleware = Throwing(new TaskCanceledException("cancelled"));
        var context = MakeContext();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.RequestTimeout, context.Response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="GlobalExceptionMiddleware" /> whose next delegate throws.</summary>
    private static GlobalExceptionMiddleware Throwing(Exception ex)
    {
        return new GlobalExceptionMiddleware(_ => throw ex);
    }

    /// <summary>Creates a <see cref="DefaultHttpContext" /> with a writable memory-stream response body.</summary>
    private static DefaultHttpContext MakeContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>Rewinds the response body stream and reads it as a string.</summary>
    private static async Task<string> ReadBodyAsync(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }
}
