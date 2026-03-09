using StewardessMCPService.CodeIndexing.Eligibility;

namespace StewardessMCPService.CodeIndexing.Source;

/// <summary>
/// Provides file enumeration and content retrieval for a repository root.
/// </summary>
public interface ISourceProvider
{
    /// <summary>
    /// Enumerates all files under <paramref name="rootPath"/> that pass the eligibility policy.
    /// </summary>
    /// <param name="rootPath">Absolute path to the repository root.</param>
    /// <param name="policy">Policy used to filter ineligible files.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<SourceFileInfo>> EnumerateFilesAsync(
        string rootPath,
        IEligibilityPolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Reads and decodes the content of a file.
    /// </summary>
    Task<SourceFileContent> ReadFileAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Computes a SHA-256 hex hash for the given raw bytes.
    /// </summary>
    string ComputeHash(byte[] content);

    /// <summary>
    /// Returns <see langword="true"/> if the byte sequence appears to be binary (non-text) content.
    /// </summary>
    bool IsBinaryContent(byte[] sample);
}
