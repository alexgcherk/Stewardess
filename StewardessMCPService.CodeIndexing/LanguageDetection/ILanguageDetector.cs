// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.LanguageDetection;

/// <summary>
///     Detects the language of a file from its extension and optional content hints.
/// </summary>
public interface ILanguageDetector
{
    /// <summary>All language IDs this detector can identify.</summary>
    IReadOnlyCollection<string> SupportedLanguageIds { get; }

    /// <summary>
    ///     Detects the language for the given file path.
    /// </summary>
    /// <param name="filePath">Path to the file (used for extension-based detection).</param>
    /// <param name="contentHint">
    ///     Optional first line(s) of the file content for shebang or content-based detection.
    /// </param>
    /// <returns>Detection result. Never returns <see langword="null" />.</returns>
    LanguageDetectionResult Detect(string filePath, string? contentHint = null);
}