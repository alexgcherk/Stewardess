// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.Model.Snapshots;

/// <summary>
///     Describes how the index was built for a snapshot.
/// </summary>
public enum IndexMode
{
    /// <summary>Complete rebuild of all files in the root path.</summary>
    Full,

    /// <summary>Only changed, added, or removed files were re-processed.</summary>
    Incremental,

    /// <summary>Only file outline (structural nodes) was extracted; no symbols.</summary>
    OutlineOnly,

    /// <summary>Declarations extracted; references not resolved.</summary>
    Declarations
}