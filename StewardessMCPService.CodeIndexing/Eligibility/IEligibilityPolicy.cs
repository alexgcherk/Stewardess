// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Eligibility;

/// <summary>
///     Determines whether a file should be included in the indexing pipeline.
/// </summary>
public interface IEligibilityPolicy
{
    /// <summary>Maximum file size (bytes) this policy will accept. Larger files are <see cref="EligibilityStatus.TooLarge" />.</summary>
    long MaxFileSizeBytes { get; }

    /// <summary>
    ///     Evaluates whether the specified file should be indexed.
    /// </summary>
    /// <param name="filePath">Absolute or repository-relative path.</param>
    /// <param name="sizeBytes">File size in bytes.</param>
    /// <param name="isBinary">Whether the file was detected as binary content.</param>
    /// <returns>Eligibility determination. Never returns <see langword="null" />.</returns>
    EligibilityResult Evaluate(string filePath, long sizeBytes, bool isBinary);
}