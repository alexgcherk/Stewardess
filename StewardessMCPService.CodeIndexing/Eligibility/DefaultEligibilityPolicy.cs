// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Eligibility;

/// <summary>
///     Default eligibility policy that applies the standard file filtering rules:
///     binary files, oversized files, build outputs, vendor directories, generated/minified files, and hidden files.
/// </summary>
public sealed class DefaultEligibilityPolicy : IEligibilityPolicy
{
    private const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    // Folders that are always excluded (lower-case, path segment matching)
    private static readonly HashSet<string> _ignoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", ".vscode",
        "node_modules", "packages", ".nuget",
        "vendor", "third_party", "thirdparty",
        "dist", "build", "out", "output",
        ".mcp_backups", ".mcp",
        "testresults", ".testresults",
        "coverage",
        "__pycache__", ".pytest_cache",
        "target", // Rust / Maven
        ".gradle"
    };

    // Extensions always excluded
    private static readonly HashSet<string> _ignoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".lib", ".a", ".so", ".dylib",
        ".msi", ".msix", ".nupkg",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
        ".bin", ".cache", ".suo", ".user",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx",
        ".mp3", ".mp4", ".avi", ".mov",
        ".ttf", ".woff", ".woff2", ".eot"
    };

    /// <summary>
    ///     Creates a policy with the default 5 MB file size limit.
    /// </summary>
    public DefaultEligibilityPolicy() : this(DefaultMaxFileSizeBytes)
    {
    }

    /// <summary>
    ///     Creates a policy with a custom file size limit.
    /// </summary>
    public DefaultEligibilityPolicy(long maxFileSizeBytes)
    {
        MaxFileSizeBytes = maxFileSizeBytes > 0
            ? maxFileSizeBytes
            : DefaultMaxFileSizeBytes;
    }

    /// <inheritdoc />
    public long MaxFileSizeBytes { get; }

    /// <inheritdoc />
    public EligibilityResult Evaluate(string filePath, long sizeBytes, bool isBinary)
    {
        // 1. Binary content
        if (isBinary)
            return EligibilityResult.Exclude(EligibilityStatus.Binary, "Binary file detected");

        // 2. Hidden or temp files
        var fileName = Path.GetFileName(filePath);
        if (IsHiddenOrTemp(fileName))
            return EligibilityResult.Exclude(EligibilityStatus.Hidden, "Hidden or temporary file");

        // 3. Ignored extension
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && _ignoredExtensions.Contains(ext))
            return EligibilityResult.Exclude(EligibilityStatus.Excluded, $"Extension '{ext}' is excluded");

        // 4. Ignored folder (check each path segment)
        if (IsInIgnoredFolder(filePath))
            return EligibilityResult.Exclude(EligibilityStatus.Ignored, "File is in an ignored directory");

        // 5. File too large
        if (sizeBytes > MaxFileSizeBytes)
            return EligibilityResult.Exclude(EligibilityStatus.TooLarge,
                $"File size {sizeBytes:N0} bytes exceeds limit {MaxFileSizeBytes:N0} bytes");

        // 6. Generated file heuristic (check file name hints)
        if (IsLikelyGenerated(fileName))
            return EligibilityResult.Exclude(EligibilityStatus.Generated, "File appears to be auto-generated");

        return EligibilityResult.Eligible();
    }

    private static bool IsHiddenOrTemp(string fileName)
    {
        return fileName.StartsWith('.') ||
               fileName.EndsWith('~') ||
               fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInIgnoredFolder(string filePath)
    {
        // Normalize separators and check each directory segment
        var normalized = filePath.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Check all segments except the last (which is the file name)
        for (var i = 0; i < parts.Length - 1; i++)
            if (_ignoredFolders.Contains(parts[i]))
                return true;
        return false;
    }

    private static bool IsLikelyGenerated(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        return lower.EndsWith(".designer.cs") ||
               lower.EndsWith(".g.cs") ||
               lower.EndsWith(".generated.cs") ||
               lower.EndsWith(".g.i.cs") ||
               lower == "assemblyinfo.cs" ||
               lower == "assemblyattributes.cs" ||
               lower.Contains(".min.js") ||
               lower.Contains(".min.css") ||
               lower.Contains(".bundle.js");
    }
}