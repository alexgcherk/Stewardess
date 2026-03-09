using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Model.References;

/// <summary>
/// Represents a resolved or partially resolved reference relationship between symbols.
/// Forms the edges of the repository reference graph.
/// </summary>
public sealed class ReferenceEdge
{
    /// <summary>Unique edge identifier.</summary>
    public required string EdgeId { get; init; }

    /// <summary>Source symbol ID (the symbol making the reference).</summary>
    public string? SourceSymbolId { get; init; }

    /// <summary>Source occurrence ID (the specific declaration making the reference).</summary>
    public string? SourceOccurrenceId { get; init; }

    /// <summary>Target symbol ID (the symbol being referenced), if resolved.</summary>
    public string? TargetSymbolId { get; init; }

    /// <summary>Semantic relationship kind.</summary>
    public RelationshipKind RelationshipKind { get; init; }

    /// <summary>How confidently this reference was resolved.</summary>
    public ResolutionClass ResolutionClass { get; init; }

    /// <summary>The source text evidence for this reference (e.g., "MyBase", "IDisposable").</summary>
    public string? Evidence { get; init; }

    /// <summary>Source span in the file where the evidence text appears.</summary>
    public SourceSpan? EvidenceSpan { get; init; }

    /// <summary>Language this reference was extracted from.</summary>
    public required string LanguageId { get; init; }

    /// <summary>How this reference was extracted.</summary>
    public ExtractionMode ExtractionMode { get; init; }

    /// <summary>Confidence in [0.0, 1.0].</summary>
    public double Confidence { get; init; } = 1.0;
}
