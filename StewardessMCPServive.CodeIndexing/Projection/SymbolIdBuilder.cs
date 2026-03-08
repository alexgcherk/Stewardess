using StewardessMCPServive.CodeIndexing.Model.Semantic;

namespace StewardessMCPServive.CodeIndexing.Projection;

/// <summary>
/// Builds stable, deterministic logical symbol identifiers from known components.
/// </summary>
/// <remarks>
/// Logical identity is based on language, repository scope, kind category, and qualified name.
/// Line numbers and file paths MUST NOT contribute to logical symbol identity.
/// Symbol IDs remain stable if a symbol is moved within a file without changing its
/// qualified name or semantic kind.
/// </remarks>
public static class SymbolIdBuilder
{
    /// <summary>
    /// Builds the symbolId string.
    /// Format: <c>{languageId}:{repoScope}:{kindCategory}:{qualifiedName}</c>.
    /// </summary>
    /// <param name="languageId">Language identifier (e.g., "csharp").</param>
    /// <param name="repoScope">Short repository scope identifier (e.g., "myrepo").</param>
    /// <param name="kindCategory">Kind category string from <see cref="GetKindCategory"/>.</param>
    /// <param name="qualifiedName">Fully qualified symbol name (e.g., "MyApp.Domain.User").</param>
    public static string BuildSymbolId(
        string languageId, string repoScope, string kindCategory, string qualifiedName) =>
        $"{languageId}:{repoScope}:{kindCategory}:{qualifiedName}";

    /// <summary>
    /// Builds the symbolKey string.
    /// Format: <c>{repoScope}|{languageId}|{kindCategory}|{qualifiedName}</c>.
    /// </summary>
    /// <param name="languageId">Language identifier.</param>
    /// <param name="repoScope">Short repository scope identifier.</param>
    /// <param name="kindCategory">Kind category string from <see cref="GetKindCategory"/>.</param>
    /// <param name="qualifiedName">Fully qualified symbol name.</param>
    public static string BuildSymbolKey(
        string languageId, string repoScope, string kindCategory, string qualifiedName) =>
        $"{repoScope}|{languageId}|{kindCategory}|{qualifiedName}";

    /// <summary>
    /// Derives a short repository scope identifier from an absolute root path.
    /// Uses the last path segment (folder name), lower-cased and sanitized.
    /// </summary>
    /// <param name="rootPath">Absolute repository root path.</param>
    /// <returns>Short repository scope string safe for use in symbol IDs.</returns>
    public static string DeriveRepoScope(string rootPath)
    {
        var normalized = rootPath.TrimEnd('/', '\\');
        var sep = normalized.LastIndexOfAny(['/', '\\']);
        var name = sep >= 0 ? normalized[(sep + 1)..] : normalized;
        // Lowercase and replace non-alphanumeric characters with dashes
        return new string(name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
    }

    /// <summary>
    /// Returns the kind category string used in symbol IDs for the given <see cref="SymbolKind"/>.
    /// Kind categories group related symbol kinds into stable ID segments.
    /// </summary>
    /// <param name="kind">The logical symbol kind.</param>
    /// <returns>A short category string: "ns", "mod", "type", "callable", "member", or "sym".</returns>
    public static string GetKindCategory(SymbolKind kind) => kind switch
    {
        SymbolKind.Namespace => "ns",
        SymbolKind.Package or SymbolKind.Module or SymbolKind.Script => "mod",
        SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
            or SymbolKind.Enum or SymbolKind.Record or SymbolKind.Trait
            or SymbolKind.Union or SymbolKind.TypeAlias or SymbolKind.Object => "type",
        SymbolKind.Method or SymbolKind.Function or SymbolKind.Constructor
            or SymbolKind.Procedure or SymbolKind.Getter or SymbolKind.Setter => "callable",
        SymbolKind.Property or SymbolKind.Field or SymbolKind.Constant
            or SymbolKind.Event or SymbolKind.Variable => "member",
        _ => "sym",
    };
}
