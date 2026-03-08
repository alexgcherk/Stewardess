namespace StewardessMCPServive.CodeIndexing.Model.Diagnostics;

/// <summary>
/// Identifies which pipeline stage produced an <see cref="IndexDiagnostic"/>.
/// </summary>
public enum DiagnosticSource
{
    EligibilityFilter,
    LanguageDetector,
    ParserAdapter,
    DeclarationProjection,
    ReferenceExtraction,
    Resolution,
    Storage,
    Indexing,
}
