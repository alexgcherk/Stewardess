namespace StewardessMCPServive.CodeIndexing.Model.Structural;

/// <summary>
/// Identifies the extraction method used to produce a structural node or symbol.
/// </summary>
public enum ExtractionMode
{
    /// <summary>Full compiler semantic analysis (e.g., Roslyn semantic model).</summary>
    CompilerSemantic,

    /// <summary>Compiler syntax tree only (e.g., Roslyn SyntaxTree without semantic model).</summary>
    CompilerSyntax,

    /// <summary>Dedicated parser-based structural extraction (e.g., Tree-sitter).</summary>
    ParserStructural,

    /// <summary>Regex or heuristic-based extraction. Inherently low confidence.</summary>
    Heuristic,
}
