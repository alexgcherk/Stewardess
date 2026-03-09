namespace StewardessMCPService.CodeIndexing.Model.Structural;

/// <summary>
/// Parse status for an individual file after extraction attempt.
/// </summary>
public enum ParseStatus
{
    /// <summary>File was fully parsed without errors.</summary>
    Success,
    /// <summary>File was partially parsed; some nodes may be missing or incomplete.</summary>
    Partial,
    /// <summary>Parser failed entirely; no structural nodes were produced.</summary>
    Failed,
    /// <summary>File was skipped (ineligible, language unsupported, or cancelled).</summary>
    Skipped,
}
