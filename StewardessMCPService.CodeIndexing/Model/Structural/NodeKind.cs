namespace StewardessMCPService.CodeIndexing.Model.Structural;

/// <summary>
/// Discriminates the broad structural category of a <see cref="StructuralNode"/>.
/// </summary>
public enum NodeKind
{
    /// <summary>Root file node.</summary>
    File,
    /// <summary>Container: namespace, package, module, script scope.</summary>
    Container,
    /// <summary>Type-like declaration: class, struct, interface, enum, record, trait.</summary>
    Declaration,
    /// <summary>Callable: method, function, constructor, procedure, accessor.</summary>
    Callable,
    /// <summary>Member: field, property, event, constant, variable.</summary>
    Member,
    /// <summary>Import/using/include/require directive.</summary>
    Import,
    /// <summary>Document section (Markdown heading, XML element).</summary>
    Section,
    /// <summary>Generic named block (PowerShell script block, SQL batch).</summary>
    Block,
    /// <summary>CSS selector.</summary>
    Selector,
    /// <summary>HTML/XML tag.</summary>
    Tag,
    /// <summary>JSON property.</summary>
    Property,
    /// <summary>JSON or similar object literal node.</summary>
    Object,
    /// <summary>JSON or similar array node.</summary>
    Array,
    /// <summary>Generic document node without a more specific classification.</summary>
    DocumentNode,
}
