using StewardessMCPServive.CodeIndexing.Model.References;
using StewardessMCPServive.CodeIndexing.Model.Structural;

namespace StewardessMCPServive.CodeIndexing.Parsers.Abstractions;

/// <summary>
/// An unresolved reference hint produced by a parser adapter during structural analysis.
/// Hints are resolved to full <see cref="ReferenceEdge"/> objects by the indexing engine
/// after symbol projection completes.
/// </summary>
public sealed class ReferenceHint
{
    /// <summary>
    /// Qualified path of the structural node that is the source of this reference
    /// (e.g., "MyApp.Domain.Customer" or "MyApp.Domain.Customer.GetOrder").
    /// Used by the indexing engine to look up the source logical symbol.
    /// </summary>
    public required string SourceQualifiedPath { get; init; }

    /// <summary>Semantic relationship kind between source and target.</summary>
    public required RelationshipKind Kind { get; init; }

    /// <summary>Raw unresolved name of the target type or module as written in source.</summary>
    public required string TargetName { get; init; }

    /// <summary>
    /// Full source text evidence (e.g., "List&lt;Customer&gt;" for a field type,
    /// "IDisposable" for an interface implementation).
    /// </summary>
    public string? Evidence { get; init; }

    /// <summary>Source location in the file where the evidence text appears.</summary>
    public SourceSpan? EvidenceSpan { get; init; }
}
