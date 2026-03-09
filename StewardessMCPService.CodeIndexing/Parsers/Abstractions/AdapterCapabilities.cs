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

    public bool SupportsOutline { get; init; }
    public bool SupportsDeclarations { get; init; }
    public bool SupportsLogicalSymbols { get; init; }
    public bool SupportsOccurrences { get; init; }
    public bool SupportsImportsOrUses { get; init; }
    public bool SupportsTypeExtraction { get; init; }
    public bool SupportsCallableExtraction { get; init; }
    public bool SupportsMemberExtraction { get; init; }
    public bool SupportsReferenceExtraction { get; init; }
    public bool SupportsRepositoryResolution { get; init; }
    public bool SupportsCrossFileResolution { get; init; }
    public bool SupportsHeuristicFallback { get; init; }
    public bool SupportsSyntaxErrorRecovery { get; init; }
    public bool SupportsDocumentTreeOnly { get; init; }

    /// <summary>Human-readable notes about guarantees and known limitations.</summary>
    public string? GuaranteeNotes { get; init; }
}
