namespace StewardessMCPService.CodeIndexing.Model.References;

/// <summary>
/// Describes the semantic relationship between a source and target symbol in a reference edge.
/// </summary>
public enum RelationshipKind
{
    // --- Type hierarchy ---
    Inherits,
    Implements,

    // --- Member type relationships ---
    ContainsFieldOfType,
    ContainsPropertyOfType,
    ReturnsType,
    AcceptsParameterType,
    UsesLocalType,
    UsesGenericArgumentType,

    // --- Instantiation / usage ---
    CreatesInstanceOf,
    ReferencesStaticMemberOf,
    UsesAttributeOrAnnotation,

    // --- Module / import ---
    ImportsModule,
    UsesNamespace,
    AliasesSymbol,

    // --- Nested / containment ---
    DefinesNestedType,

    // --- Callable / member reference ---
    ReferencesCallable,
    ReferencesMember,
    ReferencesTypeAlias,

    // --- Database objects ---
    ReferencesTable,
    ReferencesView,
    ReferencesStoredProcedure,
}
