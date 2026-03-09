// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Projection;

/// <summary>
///     Projects structural nodes extracted from a single file into logical symbols
///     and their concrete occurrences.
/// </summary>
/// <remarks>
///     One projector per language is registered. The projector maps language-specific
///     node kinds and subkinds to canonical
///     <see cref="Model.Semantic.LogicalSymbol" /> and
///     <see cref="Model.Semantic.SymbolOccurrence" /> records.
///     Projectors MUST NOT throw; any projection errors should produce partial results.
/// </remarks>
public interface ISymbolProjector
{
    /// <summary>Language ID handled by this projector.</summary>
    string LanguageId { get; }

    /// <summary>
    ///     Projects structural nodes for one file into logical symbols and occurrences.
    /// </summary>
    /// <param name="fileId">File identifier from the indexed snapshot.</param>
    /// <param name="repoScope">Short identifier for the repository scope, used in symbol IDs.</param>
    /// <param name="fileNodes">All structural nodes belonging to this file, keyed by NodeId.</param>
    /// <returns>Projected symbols and occurrences for this file.</returns>
    SymbolProjectionResult Project(
        string fileId,
        string repoScope,
        IReadOnlyDictionary<string, StructuralNode> fileNodes);
}