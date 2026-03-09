// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Mcp;
using StewardessMCPService.Models;
using Xunit;

namespace StewardessMCPService.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <see cref="McpSessionManager"/>.
    ///
    /// Covers: session lifecycle (create / get / terminate), in-flight request
    /// registration and cancellation, and the notification channel.
    /// </summary>
    public sealed class McpSessionManagerTests
    {
        private static McpSessionManager CreateManager() => new McpSessionManager();

        // ── CreateSession ────────────────────────────────────────────────────────

        [Fact]
        public void CreateSession_ReturnsSessionWithNonEmptyId()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26", "TestClient");

            Assert.NotNull(session);
            Assert.False(string.IsNullOrWhiteSpace(session.Id));
        }

        [Fact]
        public void CreateSession_IdIsUnique()
        {
            var mgr = CreateManager();

            var id1 = mgr.CreateSession("2025-03-26").Id;
            var id2 = mgr.CreateSession("2025-03-26").Id;

            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void CreateSession_StoresClientName()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26", "Claude Desktop");

            Assert.Equal("Claude Desktop", session.ClientName);
        }

        // ── TryGetSession ────────────────────────────────────────────────────────

        [Fact]
        public void TryGetSession_ReturnsSessionAfterCreate()
        {
            var mgr     = CreateManager();
            var created = mgr.CreateSession("2025-03-26");
            var found   = mgr.TryGetSession(created.Id);

            Assert.NotNull(found);
            Assert.Equal(created.Id, found!.Id);
        }

        [Fact]
        public void TryGetSession_ReturnsNullForUnknownId()
        {
            var mgr  = CreateManager();
            var sess = mgr.TryGetSession("does-not-exist");

            Assert.Null(sess);
        }

        // ── TerminateSession ─────────────────────────────────────────────────────

        [Fact]
        public void TerminateSession_RemovesSession()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26");

            mgr.TerminateSession(session.Id);

            Assert.Null(mgr.TryGetSession(session.Id));
        }

        [Fact]
        public void TerminateSession_NonExistentIdDoesNotThrow()
        {
            var mgr = CreateManager();
            // Should not throw for an ID that was never created.
            mgr.TerminateSession("ghost-session-id");
        }

        // ── RegisterInFlightRequest / CancelRequest / UnregisterInFlightRequest ──

        [Fact]
        public async Task CancelRequest_CancelsRegisteredCts()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26");

            using var cts = new CancellationTokenSource();
            mgr.RegisterInFlightRequest(session.Id, "req-1", cts);

            Assert.False(cts.IsCancellationRequested);

            mgr.CancelRequest(session.Id, "req-1");

            // Give the cancellation token a moment to propagate.
            await Task.Delay(50);
            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void CancelRequest_UnknownSessionIsNoOp()
        {
            var mgr = CreateManager();
            // Should not throw.
            mgr.CancelRequest("ghost-session", "req-1");
        }

        [Fact]
        public void CancelRequest_UnknownRequestIdIsNoOp()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26");

            // Should not throw even when no CTS is registered for this request.
            mgr.CancelRequest(session.Id, "no-such-request");
        }

        [Fact]
        public void UnregisterInFlightRequest_RemovesCtsSoSubsequentCancelIsNoOp()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26");

            using var cts = new CancellationTokenSource();
            mgr.RegisterInFlightRequest(session.Id, "req-2", cts);
            mgr.UnregisterInFlightRequest(session.Id, "req-2");

            // After unregister, cancel should not cancel the original CTS.
            mgr.CancelRequest(session.Id, "req-2");
            Assert.False(cts.IsCancellationRequested);
        }

        // ── TerminateSession cancels in-flight requests ──────────────────────────

        [Fact]
        public async Task TerminateSession_CancelsAllInFlightRequests()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26");

            using var cts1 = new CancellationTokenSource();
            using var cts2 = new CancellationTokenSource();
            mgr.RegisterInFlightRequest(session.Id, "req-a", cts1);
            mgr.RegisterInFlightRequest(session.Id, "req-b", cts2);

            mgr.TerminateSession(session.Id);

            await Task.Delay(50);
            Assert.True(cts1.IsCancellationRequested, "cts1 should have been cancelled on terminate");
            Assert.True(cts2.IsCancellationRequested, "cts2 should have been cancelled on terminate");
        }

        // ── GetNotificationChannel ───────────────────────────────────────────────

        [Fact]
        public void GetNotificationChannel_ReturnsChannelForActiveSession()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26");

            var channel = mgr.GetNotificationChannel(session.Id);

            Assert.NotNull(channel);
        }

        [Fact]
        public void GetNotificationChannel_ReturnsNullForUnknownSession()
        {
            var mgr     = CreateManager();
            var channel = mgr.GetNotificationChannel("unknown");

            Assert.Null(channel);
        }

        [Fact]
        public async Task PublishNotification_CanBeReadFromChannel()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26");

            var evt = new McpSseEvent { EventType = "message", Data = "{}" };
            mgr.PublishNotification(session.Id, evt);

            var channel = mgr.GetNotificationChannel(session.Id)!;

            // Channel should immediately have the event available.
            var received = await channel.ReadAsync(CancellationToken.None);

            Assert.Equal(evt.Data, received.Data);
        }

        [Fact]
        public void PublishNotification_ToUnknownSessionIsNoOp()
        {
            var mgr = CreateManager();
            // Should not throw.
            mgr.PublishNotification("ghost", new McpSseEvent { EventType = "message", Data = "{}" });
        }

        // ── Termination closes notification channel ──────────────────────────────

        [Fact]
        public async Task TerminateSession_CompletesNotificationChannel()
        {
            var mgr     = CreateManager();
            var session = mgr.CreateSession("2025-03-26");
            var channel = mgr.GetNotificationChannel(session.Id)!;

            mgr.TerminateSession(session.Id);

            // The channel reader should complete (ReadAllAsync enumerates zero items).
            var items = new List<McpSseEvent>();
            await foreach (var item in channel.ReadAllAsync())
                items.Add(item);

            // No items remain after termination.
            Assert.Empty(items);
        }
    }
}
