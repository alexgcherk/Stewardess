// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Diagnostics;
using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Parsers.Abstractions;

/// <summary>
/// Capabilities published by a parser adapter, describing what information
/// it can extract for its target language.
/// </summary>
public sealed class AdapterCapabilities
{
    /// <summary>Language this adapter targets. See <see cref="LanguageDetection.LanguageId"/>.</summary>
    public required string LanguageId { get; init; }

    /// <summary>Human-readable adapter version string.</summary>
    public required string AdapterVersion { get; init; }

    /// <summary>Whether the adapter can produce a structural outline (document tree).</summary>
    public bool SupportsOutline { get; init; }
    /// <summary>Whether the adapter extracts top-level declarations.</summary>
    public bool SupportsDeclarations { get; init; }
    /// <summary>Whether the adapter produces logical symbol records.</summary>
    public bool SupportsLogicalSymbols { get; init; }
    /// <summary>Whether the adapter tracks symbol occurrences (definitions and references).</summary>
    public bool SupportsOccurrences { get; init; }
    /// <summary>Whether the adapter extracts import/using/require directives.</summary>
    public bool SupportsImportsOrUses { get; init; }
    /// <summary>Whether the adapter extracts type symbols (classes, interfaces, enums, etc.).</summary>
    public bool SupportsTypeExtraction { get; init; }
    /// <summary>Whether the adapter extracts callable symbols (methods, functions, constructors).</summary>
    public bool SupportsCallableExtraction { get; init; }
    /// <summary>Whether the adapter extracts member symbols (fields, properties, events).</summary>
    public bool SupportsMemberExtraction { get; init; }
    /// <summary>Whether the adapter extracts reference edges between symbols.</summary>
    public bool SupportsReferenceExtraction { get; init; }
    /// <summary>Whether the adapter can resolve references across all files in the repository.</summary>
    public bool SupportsRepositoryResolution { get; init; }
    /// <summary>Whether the adapter resolves references that cross file boundaries.</summary>
    public bool SupportsCrossFileResolution { get; init; }
    /// <summary>Whether the adapter falls back to heuristic resolution when exact resolution fails.</summary>
    public bool SupportsHeuristicFallback { get; init; }
    /// <summary>Whether the adapter can recover from syntax errors and continue parsing.</summary>
    public bool SupportsSyntaxErrorRecovery { get; init; }
    /// <summary>Whether the adapter produces only a document tree without semantic analysis.</summary>
    public bool SupportsDocumentTreeOnly { get; init; }

    /// <summary>Human-readable notes about guarantees and known limitations.</summary>
    public string? GuaranteeNotes { get; init; }
}
