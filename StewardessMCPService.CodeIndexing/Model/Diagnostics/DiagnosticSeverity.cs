namespace StewardessMCPService.CodeIndexing.Model.Diagnostics;

/// <summary>
/// Severity level for an <see cref="IndexDiagnostic"/>.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational message.</summary>
    Info,
    /// <summary>Non-fatal warning; indexing continued with possible quality reduction.</summary>
    Warning,
    /// <summary>Error that caused partial or failed extraction for a file.</summary>
    Error,
    /// <summary>Fatal error that caused the indexing pipeline to abort.</summary>
    Fatal,
}
