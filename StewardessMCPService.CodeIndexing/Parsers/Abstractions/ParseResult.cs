// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Diagnostics;
using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Parsers.Abstractions;

/// <summary>
///     Output of a parser adapter for a single file parse operation.
/// </summary>
public sealed class ParseResult
{
    /// <summary>File ID as supplied in the request.</summary>
    public required string FileId { get; init; }

    /// <summary>Overall parse outcome.</summary>
    public ParseStatus Status { get; init; }

    /// <summary>Structural nodes extracted. Always populated even on partial parse.</summary>
    public IReadOnlyList<StructuralNode> Nodes { get; init; } = [];

    /// <summary>Import/using directives extracted (where supported).</summary>
    public IReadOnlyList<ImportEntry> Imports { get; init; } = [];

    /// <summary>Logical symbols projected (Phase 2+; empty in Phase 1).</summary>
    public IReadOnlyList<LogicalSymbol> Symbols { get; init; } = [];

    /// <summary>Symbol occurrences (Phase 2+; empty in Phase 1).</summary>
    public IReadOnlyList<SymbolOccurrence> Occurrences { get; init; } = [];

    /// <summary>Pre-resolution reference hints (Phase 3+; resolved to ReferenceEdge by IndexingEngine).</summary>
    public IReadOnlyList<ReferenceHint> ReferenceHints { get; init; } = [];

    /// <summary>Reference edges (Phase 3+; empty in Phase 1–2).</summary>
    public IReadOnlyList<ReferenceEdge> References { get; init; } = [];

    /// <summary>Diagnostics from this parse operation.</summary>
    public IReadOnlyList<IndexDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Extraction mode used.</summary>
    public ExtractionMode ExtractionMode { get; init; }

    /// <summary>Version of the adapter that produced this result.</summary>
    public string AdapterVersion { get; init; } = "0.0.0";
}