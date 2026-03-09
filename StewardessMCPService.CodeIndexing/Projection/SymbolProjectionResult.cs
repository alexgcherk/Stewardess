// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Semantic;

namespace StewardessMCPService.CodeIndexing.Projection;

/// <summary>
/// Holds the logical symbols and symbol occurrences produced by a single
/// <see cref="ISymbolProjector.Project"/> call for one file.
/// </summary>
public sealed class SymbolProjectionResult
{
    /// <summary>Logical symbols projected from the file's structural nodes.</summary>
    public IReadOnlyList<LogicalSymbol> Symbols { get; init; } = [];

    /// <summary>Declaration occurrences for the projected symbols.</summary>
    public IReadOnlyList<SymbolOccurrence> Occurrences { get; init; } = [];
}
