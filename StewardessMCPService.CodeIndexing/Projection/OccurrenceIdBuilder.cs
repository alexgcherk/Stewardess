// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Projection;

/// <summary>
///     Builds deterministic occurrence identifiers for symbol occurrences.
/// </summary>
/// <remarks>
///     Occurrence identity includes the symbol ID, file, source span, and role.
///     It is stable within a single index build but may change if the file is
///     re-indexed and line positions shift.
/// </remarks>
public static class OccurrenceIdBuilder
{
    /// <summary>
    ///     Builds an occurrence ID from the symbol ID, file ID, source span, and occurrence role.
    ///     Format: <c>{symbolId}::occ::{fileId}::{startLine}-{startCol}::{role}</c>.
    /// </summary>
    /// <param name="symbolId">The logical symbol ID this occurrence belongs to.</param>
    /// <param name="fileId">The file ID where this occurrence appears.</param>
    /// <param name="span">The source span of this occurrence.</param>
    /// <param name="role">The role this occurrence plays for its symbol.</param>
    public static string BuildOccurrenceId(
        string symbolId, string fileId, SourceSpan span, OccurrenceRole role)
    {
        return $"{symbolId}::occ::{fileId}::{span.StartLine}-{span.StartColumn}::{role}";
    }
}