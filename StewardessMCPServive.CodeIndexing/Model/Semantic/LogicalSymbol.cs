using StewardessMCPServive.CodeIndexing.Model.Structural;

namespace StewardessMCPServive.CodeIndexing.Model.Semantic;

/// <summary>
/// Represents a query-facing semantic symbol projected from the structural tree.
/// A logical symbol identifies a semantic entity (e.g., a class, method, namespace)
/// independently of its concrete file location.
/// </summary>
/// <remarks>
/// Logical symbol identity is stable: moving a symbol within a file or renaming its
/// containing file SHOULD NOT change the symbol ID if the semantic identity is preserved.
/// Line numbers MUST NOT be part of logical symbol identity.
/// </remarks>
public sealed class LogicalSymbol
{
    /// <summary>Globally unique symbol identifier. Format: {language}:{repo}:{kind}:{qualifiedName}.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Human-readable composite key. Format: {repo}|{language}|{kind}|{qualifiedName}.</summary>
    public required string SymbolKey { get; init; }

    /// <summary>Simple unqualified name (e.g., "User").</summary>
    public required string Name { get; init; }

    /// <summary>Fully qualified name (e.g., "MyApp.Domain.User").</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Display-friendly representation including generic parameters.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Semantic kind of this symbol.</summary>
    public SymbolKind Kind { get; init; }

    /// <summary>Language-specific sub-kind string (e.g., "record class").</summary>
    public string? Subkind { get; init; }

    /// <summary>Language identifier.</summary>
    public required string LanguageId { get; init; }

    /// <summary>File ID of the primary occurrence.</summary>
    public required string PrimaryFileId { get; init; }

    /// <summary>Occurrence ID of the primary (canonical) declaration.</summary>
    public required string PrimaryOccurrenceId { get; init; }

    /// <summary>Symbol ID of the containing symbol (e.g., namespace or parent class), if any.</summary>
    public string? ParentSymbolId { get; init; }

    /// <summary>Ordered container path from root to immediate parent (e.g., ["MyApp", "Domain"]).</summary>
    public IReadOnlyList<string> ContainerPath { get; init; } = [];

    /// <summary>Visibility keyword (e.g., "public", "internal", "private").</summary>
    public string? Visibility { get; init; }

    /// <summary>Language modifiers (e.g., "static", "abstract", "sealed", "async").</summary>
    public IReadOnlyList<string> Modifiers { get; init; } = [];

    /// <summary>Generic type parameter names (e.g., ["T", "TKey", "TValue"]).</summary>
    public IReadOnlyList<string> GenericParameters { get; init; } = [];

    /// <summary>Human-readable signature or declaration display string.</summary>
    public string? SignatureDisplay { get; init; }

    /// <summary>Documentation summary extracted from XML doc comments or docstrings.</summary>
    public string? DocumentationSummary { get; init; }

    /// <summary>How this symbol was extracted.</summary>
    public ExtractionMode ExtractionMode { get; init; }

    /// <summary>Extraction confidence in [0.0, 1.0].</summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Capability flags indicating what data is available for this symbol.</summary>
    public CapabilityFlags CapabilityFlags { get; init; }
}
