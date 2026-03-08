namespace StewardessMCPServive.CodeIndexing.Model.Semantic;

/// <summary>
/// Role of a <see cref="SymbolOccurrence"/> within its file.
/// Supports partial classes, header/source separation, and distributed declarations.
/// </summary>
public enum OccurrenceRole
{
    /// <summary>Primary declaration (e.g., C# class declaration).</summary>
    Declaration,

    /// <summary>Definition with implementation body (e.g., C++ function definition).</summary>
    Definition,

    /// <summary>Interface or abstract method implementation.</summary>
    Implementation,

    /// <summary>Partial class/struct fragment.</summary>
    Partial,

    /// <summary>Forward declaration (e.g., C/C++ forward declaration).</summary>
    ForwardDeclaration,

    /// <summary>Merged declaration spanning multiple files or compilation units.</summary>
    MergedDeclaration,
}
