// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using StewardessMCPService.CodeIndexing.Eligibility;

namespace StewardessMCPService.CodeIndexing.Source;

/// <summary>
/// File system-based source provider. Enumerates files recursively,
/// reads content with encoding detection, and computes SHA-256 hashes.
/// </summary>
public sealed class FileSystemSourceProvider : ISourceProvider
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<SourceFileInfo>> EnumerateFilesAsync(
        string rootPath,
        IEligibilityPolicy policy,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Repository root not found: {rootPath}");

        var results = new List<SourceFileInfo>();

        await Task.Run(() =>
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(file);
                    var sizeBytes = info.Length;

                    // Quick binary pre-check using first 512 bytes (no full read needed for eligibility)
                    bool isBinary = QuickBinaryCheck(file);

                    var eligibility = policy.Evaluate(file, sizeBytes, isBinary);
                    if (!eligibility.IsEligible) continue;

                    results.Add(new SourceFileInfo
                    {
                        FilePath = file,
                        SizeBytes = sizeBytes,
                        LastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                    });
                }
                catch (Exception)
                {
                    // Skip files we cannot access (locked, permission denied, etc.)
                }
            }
        }, ct);

        return results;
    }

    /// <inheritdoc/>
    public async Task<SourceFileContent> ReadFileAsync(string filePath, CancellationToken ct = default)
    {
        var rawBytes = await File.ReadAllBytesAsync(filePath, ct);
        var (content, encoding) = DecodeContent(rawBytes);
        var hash = ComputeHash(rawBytes);

        return new SourceFileContent
        {
            FilePath = filePath,
            Content = content,
            Encoding = encoding,
            ContentHash = hash,
            RawBytes = rawBytes,
        };
    }

    /// <inheritdoc/>
    public string ComputeHash(byte[] content)
    {
        var hashBytes = SHA256.HashData(content);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <inheritdoc/>
    public bool IsBinaryContent(byte[] sample)
    {
        // Check for null bytes or high proportion of non-printable characters
        int checkLength = Math.Min(sample.Length, 8000);
        int nonPrintable = 0;
        for (int i = 0; i < checkLength; i++)
        {
            byte b = sample[i];
            if (b == 0) return true;         // Null byte = definite binary
            if (b < 8 || (b > 13 && b < 32)) nonPrintable++;
        }
        return checkLength > 0 && (nonPrintable * 100 / checkLength) > 30;
    }

    // Reads a few hundred bytes to decide binary without reading the whole file
    private bool QuickBinaryCheck(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[512];
            int read = fs.Read(buffer, 0, buffer.Length);
            if (read == 0) return false;
            return IsBinaryContent(buffer[..read]);
        }
        catch
        {
            return false;
        }
    }

    private static (string content, string encoding) DecodeContent(byte[] rawBytes)
    {
        // BOM detection
        if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(rawBytes, 3, rawBytes.Length - 3), "utf-8-bom");

        if (rawBytes.Length >= 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(rawBytes, 2, rawBytes.Length - 2), "utf-16-le");

        if (rawBytes.Length >= 2 && rawBytes[0] == 0xFE && rawBytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(rawBytes, 2, rawBytes.Length - 2), "utf-16-be");

        // Default UTF-8 (with replacement character for invalid sequences)
        return (Encoding.UTF8.GetString(rawBytes), "utf-8");
    }
}
