namespace StewardessMCPService.CodeIndexing.Model.Semantic;

/// <summary>
/// Semantic kind of a logical symbol. Maps language-specific constructs to a
/// canonical vocabulary.
/// </summary>
public enum SymbolKind
{
    // --- Container / scope symbols ---
    Namespace,
    Package,
    Module,
    Script,

    // --- Type symbols ---
    Class,
    Struct,
    Interface,
    Enum,
    Record,
    Trait,
    Object,
    Union,
    TypeAlias,

    // --- Callable symbols ---
    Method,
    Function,
    Constructor,
    Procedure,
    Getter,
    Setter,

    // --- Member symbols ---
    Property,
    Field,
    Constant,
    Event,
    Variable,

    // --- SQL / DB symbols ---
    Table,
    View,
    StoredProcedure,
    FunctionObject,

    // --- Document symbols ---
    DocumentSection,
}
