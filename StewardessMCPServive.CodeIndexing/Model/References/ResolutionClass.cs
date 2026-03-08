namespace StewardessMCPServive.CodeIndexing.Model.References;

/// <summary>
/// Classifies how confidently a reference was resolved to a target symbol.
/// </summary>
/// <remarks>
/// Only <see cref="ExactBound"/>, <see cref="ScopedBound"/>, <see cref="ImportBound"/>,
/// and <see cref="AliasBound"/> produce hard dependency edges by default.
/// </remarks>
public enum ResolutionClass
{
    /// <summary>Resolved via exact fully-qualified match.</summary>
    ExactBound,

    /// <summary>Resolved by same-scope or container-level matching.</summary>
    ScopedBound,

    /// <summary>Resolved through an import/using/require statement.</summary>
    ImportBound,

    /// <summary>Resolved through an alias (e.g., using Alias = Type).</summary>
    AliasBound,

    /// <summary>Multiple possible targets exist; not safe as a hard edge.</summary>
    Ambiguous,

    /// <summary>Target symbol is outside this repository.</summary>
    External,

    /// <summary>Could not resolve target symbol.</summary>
    Unknown,
}
