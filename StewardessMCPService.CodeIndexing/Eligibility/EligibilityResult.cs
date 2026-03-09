// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Eligibility;

/// <summary>
/// Result of an eligibility evaluation for a candidate file.
/// </summary>
public sealed class EligibilityResult
{
    /// <summary>Determined eligibility status.</summary>
    public EligibilityStatus Status { get; private init; }

    /// <summary>Human-readable reason for non-eligible status, or <see langword="null"/> when eligible.</summary>
    public string? Reason { get; private init; }

    /// <summary>Returns <see langword="true"/> when the file should be indexed.</summary>
    public bool IsEligible => Status == EligibilityStatus.Eligible;

    /// <summary>Creates an eligible result.</summary>
    public static EligibilityResult Eligible() => new() { Status = EligibilityStatus.Eligible };

    /// <summary>Creates an excluded result with a reason.</summary>
    public static EligibilityResult Exclude(EligibilityStatus status, string reason) =>
        new() { Status = status, Reason = reason };
}
