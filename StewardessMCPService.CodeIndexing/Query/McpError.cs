// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
namespace StewardessMCPService.CodeIndexing.Query;

/// <summary>
/// Structured error response for MCP tools, enabling machine-classifiable error handling.
/// </summary>
/// <param name="Code">Machine-readable error code from <see cref="McpErrorCode"/>.</param>
/// <param name="Message">Human-readable description of the error.</param>
/// <param name="Context">Optional structured context (e.g. symbolId, filePath) that caused the error.</param>
public record McpError(string Code, string Message, object? Context = null);
