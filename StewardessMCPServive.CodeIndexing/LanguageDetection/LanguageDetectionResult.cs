namespace StewardessMCPServive.CodeIndexing.LanguageDetection;

/// <summary>
/// Result of language detection for a file.
/// </summary>
public sealed class LanguageDetectionResult
{
    /// <summary>Detected language identifier. See <see cref="LanguageId"/> for well-known values.</summary>
    public required string LanguageId { get; init; }

    /// <summary>Method used to determine language: "extension", "shebang", "content", "default".</summary>
    public required string DetectionMethod { get; init; }

    /// <summary>Detection confidence in [0.0, 1.0].</summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Returns <see langword="true"/> when the language was successfully identified.</summary>
    public bool IsKnown => LanguageId != LanguageDetection.LanguageId.Unknown;
}
