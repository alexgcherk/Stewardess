// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.Model.Structural;

/// <summary>
///     Controls how deeply the parser adapter processes a file.
/// </summary>
public enum ParseMode
{
    /// <summary>Extract top-level structural outline only (fastest).</summary>
    OutlineOnly,

    /// <summary>Extract all declarations and their containment hierarchy.</summary>
    Declarations,

    /// <summary>Declarations plus candidate reference extraction from signatures.</summary>
    DeclarationsAndReferences,

    /// <summary>Full parsing with body-level references where the adapter supports it.</summary>
    FullConfiguredMode
}