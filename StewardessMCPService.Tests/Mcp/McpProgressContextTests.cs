// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.Mcp;
using StewardessMCPService.Models;
using Xunit;

namespace StewardessMCPService.Tests.Mcp;

/// <summary>
///     Unit tests for <see cref="McpProgressContext" />.
///     Verifies the AsyncLocal isolation and the ReportProgress helper behaviour.
/// </summary>
public sealed class McpProgressContextTests
{
    // ── Default state ────────────────────────────────────────────────────────

    [Fact]
    public void Current_IsNullByDefault()
    {
        // Each test gets its own async context, so there should be no reporter.
        McpProgressContext.Current = null;
        Assert.Null(McpProgressContext.Current);
    }

    // ── ReportProgress no-op when no reporter ────────────────────────────────

    [Fact]
    public void ReportProgress_IsNoOpWhenNoReporterInstalled()
    {
        McpProgressContext.Current = null;

        // Must not throw.
        McpProgressContext.ReportProgress(0.5, 1.0, "halfway");
    }

    // ── Reporter receives correct values ─────────────────────────────────────

    [Fact]
    public void ReportProgress_InvokesReporterWithCorrectValues()
    {
        var received = new List<McpProgressEvent>();
        McpProgressContext.Current = new Progress<McpProgressEvent>(e => received.Add(e));

        McpProgressContext.ReportProgress(0.3, 1.0, "processing");

        // Progress<T> raises on the ThreadPool, give it a moment.
        Thread.Sleep(50);

        Assert.Single(received);
        Assert.Equal(0.3, received[0].Progress, 3);
        Assert.Equal(1.0, (double)received[0].Total!, 3);
        Assert.Equal("processing", received[0].Message);
    }

    [Fact]
    public void ReportProgress_NullMessageIsAccepted()
    {
        var received = new List<McpProgressEvent>();
        McpProgressContext.Current = new Progress<McpProgressEvent>(e => received.Add(e));

        McpProgressContext.ReportProgress(1.0, 1.0);

        Thread.Sleep(50);

        Assert.Single(received);
        Assert.Null(received[0].Message);
    }

    // ── AsyncLocal isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task Current_IsIsolatedBetweenAsyncFlows()
    {
        // Each async branch gets its own copy of the AsyncLocal value.
        McpProgressContext.Current = null;

        var events1 = new List<McpProgressEvent>();
        var events2 = new List<McpProgressEvent>();

        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();

        // Branch 1: install a reporter, then signal.
        var branch1 = Task.Run(async () =>
        {
            McpProgressContext.Current = new Progress<McpProgressEvent>(e => events1.Add(e));
            await tcs1.Task;
            McpProgressContext.ReportProgress(0.5, 1.0, "branch1");
            Thread.Sleep(50);
        });

        // Branch 2: no reporter.
        var branch2 = Task.Run(async () =>
        {
            // Explicitly clear any inherited value.
            McpProgressContext.Current = null;
            await tcs2.Task;
            McpProgressContext.ReportProgress(0.5, 1.0, "branch2");
            Thread.Sleep(50);
        });

        tcs1.SetResult();
        tcs2.SetResult();

        await Task.WhenAll(branch1, branch2);

        // Branch 1's reporter should have received one event.
        Assert.Single(events1);
        // Branch 2 had no reporter so events2 is empty.
        Assert.Empty(events2);
    }

    [Fact]
    public void Current_CanBeRestoredAfterToolCall()
    {
        // Simulate what McpToolHandler does: save, set, call tool, restore.
        McpProgressContext.Current = null;

        var received = new List<McpProgressEvent>();

        var prev = McpProgressContext.Current;

        var reporter = new Progress<McpProgressEvent>(e => received.Add(e));
        McpProgressContext.Current = reporter;

        // Simulate tool call
        McpProgressContext.ReportProgress(0.5, 1.0, "working");
        Thread.Sleep(50);

        // Restore
        McpProgressContext.Current = prev;

        // After restore, reports should go nowhere.
        McpProgressContext.ReportProgress(1.0, 1.0, "done");
        Thread.Sleep(50);

        // Only one event (from when reporter was active).
        Assert.Single(received);
    }
}