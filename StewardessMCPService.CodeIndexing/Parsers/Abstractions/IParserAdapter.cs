// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
namespace StewardessMCPService.CodeIndexing.Parsers.Abstractions;

/// <summary>
/// Contract for a language-specific parser adapter.
/// Each adapter isolates parser implementation details and exposes a consistent extraction contract.
/// </summary>
/// <remarks>
/// Adapters MUST NOT execute user code, MUST support <see cref="CancellationToken"/> propagation,
/// and MUST return partial results rather than throwing on malformed input.
/// </remarks>
public interface IParserAdapter
{
    /// <summary>Language identifier this adapter handles. See <see cref="LanguageDetection.LanguageId"/>.</summary>
    string LanguageId { get; }

    /// <summary>Published capabilities for this adapter.</summary>
    AdapterCapabilities Capabilities { get; }

    /// <summary>
    /// Parses the file described in <paramref name="request"/> and returns extracted structure.
    /// </summary>
    /// <param name="request">Parse request with file content and configuration.</param>
    /// <param name="ct">Cancellation token for per-file timeouts and pipeline cancellation.</param>
    /// <returns>
    /// A <see cref="ParseResult"/> containing structural nodes and any diagnostics.
    /// MUST NOT return <see langword="null"/>. MUST NOT throw unless the cancellation token is cancelled.
    /// </returns>
    Task<ParseResult> ParseAsync(ParseRequest request, CancellationToken ct = default);
}
