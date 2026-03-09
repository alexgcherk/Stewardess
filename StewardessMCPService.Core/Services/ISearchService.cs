// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Text, regex, symbol, and file-name search across the repository.
    /// </summary>
    public interface ISearchService
    {
        /// <summary>Searches for a literal text string across files in the repository.</summary>
        Task<SearchResponse> SearchTextAsync(SearchTextRequest request, CancellationToken ct = default);

        /// <summary>Searches using a .NET regular expression pattern.</summary>
        Task<SearchResponse> SearchRegexAsync(SearchRegexRequest request, CancellationToken ct = default);

        /// <summary>Finds files whose names match a wildcard or substring pattern.</summary>
        Task<FileNameSearchResponse> SearchFileNamesAsync(SearchFileNamesRequest request, CancellationToken ct = default);

        /// <summary>Returns all files matching one or more extensions.</summary>
        Task<FileNameSearchResponse> SearchByExtensionAsync(SearchByExtensionRequest request, CancellationToken ct = default);

        /// <summary>
        /// Best-effort symbol search using text heuristics (class/method/interface declarations).
        /// </summary>
        Task<SearchResponse> SearchSymbolAsync(SearchSymbolRequest request, CancellationToken ct = default);

        /// <summary>
        /// Best-effort reference search — finds textual usages of an identifier.
        /// </summary>
        Task<SearchResponse> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct = default);
    }
}
