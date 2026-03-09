// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPService.Models;
using StewardessMCPService.Services;

namespace StewardessMCPService.Controllers
{
    /// <summary>
    /// Search endpoints.
    ///
    /// GET  /api/search/text        — search_text
    /// POST /api/search/text        — search_text (body params)
    /// POST /api/search/regex       — search_regex
    /// GET  /api/search/files       — search_file_names
    /// POST /api/search/extension   — search_by_extension
    /// POST /api/search/symbol      — search_symbol
    /// POST /api/search/references  — find_references
    /// </summary>
    [Route("api/search")]
    public sealed class SearchController : BaseController
    {
        private ISearchService SearchService => GetService<ISearchService>();

        // ── search_text (GET) ────────────────────────────────────────────────────

        /// <summary>Searches for a literal string (GET variant for simple queries).</summary>
        [HttpGet, Route("text")]
        public IActionResult SearchTextGet(
            [FromQuery] string q,
            [FromQuery] string path       = "",
            [FromQuery] bool ignoreCase   = true,
            [FromQuery] bool wholeWord    = false,
            [FromQuery] int maxResults    = 100,
            [FromQuery] int contextBefore = 2,
            [FromQuery] int contextAfter  = 2)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(ErrorCodes.MissingParameter, "'q' query parameter is required.");

            var request = new SearchTextRequest
            {
                Query              = q,
                SearchPath         = path,
                IgnoreCase         = ignoreCase,
                WholeWord          = wholeWord,
                MaxResults         = maxResults,
                ContextLinesBefore = contextBefore,
                ContextLinesAfter  = contextAfter
            };

            return ExecuteSearch(() => SearchService.SearchTextAsync(request, CancellationToken.None)
                                                    .GetAwaiter().GetResult());
        }

        // ── search_text (POST) ───────────────────────────────────────────────────

        /// <summary>Searches for a literal string (POST variant for full options).</summary>
        [HttpPost, Route("text")]
        public IActionResult SearchTextPost([FromBody] SearchTextRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(ErrorCodes.MissingParameter, "'query' is required.");

            return ExecuteSearch(() => SearchService.SearchTextAsync(request, CancellationToken.None)
                                                    .GetAwaiter().GetResult());
        }

        // ── search_regex ─────────────────────────────────────────────────────────

        /// <summary>Searches using a .NET regular expression pattern.</summary>
        [HttpPost, Route("regex")]
        public IActionResult SearchRegex([FromBody] SearchRegexRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Pattern))
                return BadRequest(ErrorCodes.MissingParameter, "'pattern' is required.");

            try
            {
                return ExecuteSearch(() => SearchService.SearchRegexAsync(request, CancellationToken.None)
                                                        .GetAwaiter().GetResult());
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ErrorCodes.InvalidPattern, ex.Message);
            }
        }

        // ── search_file_names ────────────────────────────────────────────────────

        /// <summary>Finds files whose names match a wildcard or substring.</summary>
        [HttpGet, Route("files")]
        public IActionResult SearchFiles(
            [FromQuery] string q,
            [FromQuery] string path      = "",
            [FromQuery] bool ignoreCase  = true,
            [FromQuery] bool fullPath    = false,
            [FromQuery] int maxResults   = 100)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(ErrorCodes.MissingParameter, "'q' is required.");

            try
            {
                var request = new SearchFileNamesRequest
                {
                    Pattern       = q,
                    SearchPath    = path,
                    IgnoreCase    = ignoreCase,
                    MatchFullPath = fullPath,
                    MaxResults    = maxResults
                };
                var result = SearchService.SearchFileNamesAsync(request, CancellationToken.None)
                                          .GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── search_by_extension ──────────────────────────────────────────────────

        /// <summary>Returns all files with the given extensions.</summary>
        [HttpPost, Route("extension")]
        public IActionResult SearchByExtension([FromBody] SearchByExtensionRequest request)
        {
            if (request?.Extensions == null || request.Extensions.Count == 0)
                return BadRequest(ErrorCodes.MissingParameter, "'extensions' must be a non-empty array.");

            try
            {
                var result = SearchService.SearchByExtensionAsync(request, CancellationToken.None)
                                          .GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── search_symbol ────────────────────────────────────────────────────────

        /// <summary>Best-effort symbol search via text heuristics.</summary>
        [HttpPost, Route("symbol")]
        public IActionResult SearchSymbol([FromBody] SearchSymbolRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SymbolName))
                return BadRequest(ErrorCodes.MissingParameter, "'symbolName' is required.");

            return ExecuteSearch(() => SearchService.SearchSymbolAsync(request, CancellationToken.None)
                                                    .GetAwaiter().GetResult());
        }

        // ── find_references ──────────────────────────────────────────────────────

        /// <summary>Finds textual references to an identifier across the repository.</summary>
        [HttpPost, Route("references")]
        public IActionResult FindReferences([FromBody] FindReferencesRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.IdentifierName))
                return BadRequest(ErrorCodes.MissingParameter, "'identifierName' is required.");

            return ExecuteSearch(() => SearchService.FindReferencesAsync(request, CancellationToken.None)
                                                    .GetAwaiter().GetResult());
        }

        // ── Private ──────────────────────────────────────────────────────────────

        private IActionResult ExecuteSearch(Func<SearchResponse> fn)
        {
            try
            {
                var result = fn();
                return Ok(result);
            }
            catch (ArgumentException ex)  { return BadRequest(ErrorCodes.InvalidRequest, ex.Message); }
            catch (Exception ex)          { return HandleException(ex); }
        }
    }
}
