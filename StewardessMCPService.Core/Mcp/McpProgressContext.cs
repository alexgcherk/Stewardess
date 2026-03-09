// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using StewardessMCPService.Models;

namespace StewardessMCPService.Mcp
{
    /// <summary>
    /// Ambient progress-reporting context for MCP tool handlers.
    ///
    /// The MCP Streamable HTTP transport (2025-03-26) allows the server to stream
    /// <c>notifications/progress</c> events back to the client during a long-running
    /// tool call.  Rather than changing every tool handler's signature, this class
    /// stores the current session's progress reporter in an <see cref="AsyncLocal{T}"/>
    /// so that any code running on the same logical async flow can report progress
    /// without needing an explicit parameter.
    ///
    /// Usage in a tool handler (inside McpToolRegistry):
    /// <code>
    ///     McpProgressContext.ReportProgress(0, 100, "Starting...");
    ///     // ... do work ...
    ///     McpProgressContext.ReportProgress(50, 100, "Halfway there");
    ///     // ... more work ...
    ///     McpProgressContext.ReportProgress(100, 100, "Done");
    /// </code>
    ///
    /// When no progress reporter is installed (i.e. the call was made via the classic
    /// <c>POST /mcp/v1/</c> endpoint that does not support streaming), all calls to
    /// <see cref="ReportProgress"/> are silently no-ops.
    /// </summary>
    public static class McpProgressContext
    {
        // AsyncLocal ensures that setting a reporter on one async flow does not affect
        // sibling flows running concurrently (e.g. two simultaneous tool calls from
        // different clients each get their own isolated reporter).
        private static readonly AsyncLocal<IProgress<McpProgressEvent>?> _current =
            new AsyncLocal<IProgress<McpProgressEvent>?>();

        /// <summary>
        /// Gets or sets the <see cref="IProgress{T}"/> reporter for the current
        /// asynchronous execution context.
        ///
        /// Set this before invoking a tool handler to route progress reports to the
        /// appropriate SSE channel.  Set to <c>null</c> to disable progress reporting.
        /// </summary>
        public static IProgress<McpProgressEvent>? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        /// <summary>
        /// Reports progress on the current execution context's reporter, if one is set.
        ///
        /// When no reporter is installed this method is a no-op; callers do not need to
        /// null-check before calling.
        /// </summary>
        /// <param name="progress">
        /// Current progress value.  When <paramref name="total"/> is provided, this
        /// should be between 0 and <paramref name="total"/> inclusive.
        /// </param>
        /// <param name="total">
        /// Optional total value that gives <paramref name="progress"/> its scale.
        /// Pass <c>null</c> when the total is not known in advance.
        /// </param>
        /// <param name="message">
        /// Optional human-readable status message, e.g. "Parsing 12 of 48 files".
        /// </param>
        public static void ReportProgress(double progress, double? total = null, string? message = null)
        {
            _current.Value?.Report(new McpProgressEvent
            {
                // ProgressToken is filled in by the IProgress<> implementation installed
                // by the controller, which knows the token from the incoming request.
                ProgressToken = string.Empty, // placeholder — overwritten by the reporter
                Progress      = progress,
                Total         = total,
                Message       = message,
            });
        }
    }
}
