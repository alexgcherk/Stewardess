// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.LanguageDetection;

/// <summary>
///     Default language detector: maps file extensions to language IDs and detects
///     shebangs for extensionless or ambiguous files.
/// </summary>
public sealed class DefaultLanguageDetector : ILanguageDetector
{
    // Extension → language ID mapping (lower-case extensions)
    private static readonly Dictionary<string, string> _extensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = LanguageId.CSharp,
        [".vb"] = LanguageId.VbNet,
        [".fs"] = LanguageId.FSharp,
        [".fsi"] = LanguageId.FSharp,
        [".fsx"] = LanguageId.FSharp,
        [".ts"] = LanguageId.TypeScript,
        [".tsx"] = LanguageId.TypeScript,
        [".js"] = LanguageId.JavaScript,
        [".jsx"] = LanguageId.JavaScript,
        [".mjs"] = LanguageId.JavaScript,
        [".cjs"] = LanguageId.JavaScript,
        [".py"] = LanguageId.Python,
        [".pyw"] = LanguageId.Python,
        [".java"] = LanguageId.Java,
        [".go"] = LanguageId.Go,
        [".rs"] = LanguageId.Rust,
        [".cpp"] = LanguageId.Cpp,
        [".cc"] = LanguageId.Cpp,
        [".cxx"] = LanguageId.Cpp,
        [".hpp"] = LanguageId.Cpp,
        [".hxx"] = LanguageId.Cpp,
        [".h"] = LanguageId.C, // Defaults to C; heuristic may upgrade to Cpp
        [".c"] = LanguageId.C,
        [".json"] = LanguageId.Json,
        [".xml"] = LanguageId.Xml,
        [".xaml"] = LanguageId.Xml,
        [".csproj"] = LanguageId.Xml,
        [".vbproj"] = LanguageId.Xml,
        [".fsproj"] = LanguageId.Xml,
        [".props"] = LanguageId.Xml,
        [".targets"] = LanguageId.Xml,
        [".html"] = LanguageId.Html,
        [".htm"] = LanguageId.Html,
        [".cshtml"] = LanguageId.Html,
        [".razor"] = LanguageId.Html,
        [".css"] = LanguageId.Css,
        [".scss"] = LanguageId.Css,
        [".sass"] = LanguageId.Css,
        [".less"] = LanguageId.Css,
        [".sql"] = LanguageId.Sql,
        [".sh"] = LanguageId.Shell,
        [".bash"] = LanguageId.Shell,
        [".zsh"] = LanguageId.Shell,
        [".ps1"] = LanguageId.PowerShell,
        [".psm1"] = LanguageId.PowerShell,
        [".psd1"] = LanguageId.PowerShell,
        [".md"] = LanguageId.Markdown,
        [".mdx"] = LanguageId.Markdown,
        [".markdown"] = LanguageId.Markdown
    };

    // Shebang prefix → language ID
    private static readonly (string Prefix, string Lang)[] _shebangMap =
    [
        ("python3", LanguageId.Python),
        ("python", LanguageId.Python),
        ("node", LanguageId.JavaScript),
        ("bash", LanguageId.Shell),
        ("sh", LanguageId.Shell),
        ("zsh", LanguageId.Shell),
        ("ruby", LanguageId.Unknown), // Not in scope, but detect to avoid Unknown
        ("perl", LanguageId.Unknown)
    ];

    private static readonly IReadOnlyCollection<string> _supportedLanguages =
        _extensionMap.Values.Distinct().ToList().AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedLanguageIds => _supportedLanguages;

    /// <inheritdoc />
    public LanguageDetectionResult Detect(string filePath, string? contentHint = null)
    {
        // 1. Extension-based detection
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && _extensionMap.TryGetValue(ext, out var langByExt))
            return new LanguageDetectionResult
            {
                LanguageId = langByExt,
                DetectionMethod = "extension",
                Confidence = 1.0
            };

        // 2. Shebang detection from content hint
        if (!string.IsNullOrEmpty(contentHint))
        {
            var shebangResult = TryDetectFromShebang(contentHint);
            if (shebangResult != null) return shebangResult;
        }

        // 3. Unknown
        return new LanguageDetectionResult
        {
            LanguageId = LanguageId.Unknown,
            DetectionMethod = "default",
            Confidence = 0.0
        };
    }

    private static LanguageDetectionResult? TryDetectFromShebang(string content)
    {
        // Read the first line
        var newline = content.IndexOfAny(['\n', '\r']);
        var firstLine = newline >= 0 ? content[..newline] : content;

        if (!firstLine.StartsWith("#!")) return null;

        var shebang = firstLine[2..].Trim().ToLowerInvariant();
        foreach (var (prefix, lang) in _shebangMap)
            if (shebang.Contains(prefix))
                return new LanguageDetectionResult
                {
                    LanguageId = lang,
                    DetectionMethod = "shebang",
                    Confidence = 0.9
                };

        return null;
    }
}