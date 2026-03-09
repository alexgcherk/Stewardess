// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Model.Diagnostics;

/// <summary>
/// A diagnostic produced during indexing, associated with a file or pipeline stage.
/// </summary>
public sealed class IndexDiagnostic
{
    /// <summary>Unique diagnostic identifier.</summary>
    public required string DiagnosticId { get; init; }

    /// <summary>Severity level.</summary>
    public DiagnosticSeverity Severity { get; init; }

    /// <summary>Pipeline stage that emitted this diagnostic.</summary>
    public DiagnosticSource Source { get; init; }

    /// <summary>Machine-readable diagnostic code (e.g., "PARSE_ERROR", "BINARY_DETECTED").</summary>
    public string? Code { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; }

    /// <summary>Repository-relative file path this diagnostic relates to, if applicable.</summary>
    public string? FilePath { get; init; }

    /// <summary>Source span within the file, if applicable.</summary>
    public SourceSpan? SourceSpan { get; init; }

    /// <summary>Related entity ID (symbol, node, etc.) if applicable.</summary>
    public string? RelatedEntityId { get; init; }
}
