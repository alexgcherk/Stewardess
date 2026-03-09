// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.Model.Structural;

/// <summary>
///     Eligibility determination for a file in the indexing pipeline.
/// </summary>
public enum EligibilityStatus
{
    /// <summary>File is eligible for indexing.</summary>
    Eligible,

    /// <summary>File explicitly excluded by configuration.</summary>
    Excluded,

    /// <summary>File is in an ignored folder (build output, vendor, etc.).</summary>
    Ignored,

    /// <summary>File detected as binary content.</summary>
    Binary,

    /// <summary>File exceeds the configured size threshold.</summary>
    TooLarge,

    /// <summary>File appears to be auto-generated.</summary>
    Generated,

    /// <summary>File appears to be minified.</summary>
    Minified,

    /// <summary>File is hidden or temporary.</summary>
    Hidden
}