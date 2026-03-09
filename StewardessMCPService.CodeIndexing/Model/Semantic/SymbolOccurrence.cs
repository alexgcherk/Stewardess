// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Model.Semantic;

/// <summary>
///     Represents one concrete declaration, definition, implementation, or partial
///     occurrence of a <see cref="LogicalSymbol" /> in a specific file.
/// </summary>
/// <remarks>
///     Multiple occurrences can map to a single logical symbol (e.g., partial classes,
///     header/source separation, distributed declarations across files).
/// </remarks>
public sealed class SymbolOccurrence
{
    /// <summary>Unique occurrence identifier.</summary>
    public required string OccurrenceId { get; init; }

    /// <summary>The logical symbol this occurrence belongs to.</summary>
    public required string SymbolId { get; init; }

    /// <summary>File ID where this occurrence appears.</summary>
    public required string FileId { get; init; }

    /// <summary>Role this occurrence plays for its symbol.</summary>
    public OccurrenceRole Role { get; init; }

    /// <summary>Source location of this occurrence.</summary>
    public required SourceSpan SourceSpan { get; init; }

    /// <summary>Whether this is the primary canonical occurrence of the symbol.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>How this occurrence was extracted.</summary>
    public ExtractionMode ExtractionMode { get; init; }

    /// <summary>Extraction confidence in [0.0, 1.0].</summary>
    public double Confidence { get; init; } = 1.0;
}