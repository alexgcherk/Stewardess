using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using StewardessMCPService.Parsers.CSharp;
using Xunit;

namespace StewardessMCPService.CodeIndexing.Tests.Parsers;

public class CSharpReferenceExtractionTests
{
    private readonly CSharpParserAdapter _adapter = new();

    private static ParseRequest MakeRequest(string content) => new()
    {
        FileId = "test-ref",
        FilePath = "Test.cs",
        Content = content,
        LanguageId = "csharp",
        Mode = StewardessMCPService.CodeIndexing.Model.Structural.ParseMode.Declarations,
    };

    // ── Base class / Inherits ──────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ClassWithBaseClass_ReturnsInheritsHint()
    {
        var content = """
            namespace MyApp;
            public class Child : Base { }
            public class Base { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.Inherits && h.TargetName == "Base");
    }

    [Fact]
    public async Task ParseAsync_ClassInheritsQualifiedBase_ReturnsInheritsHint()
    {
        var content = """
            namespace MyApp;
            public class Child : MyApp.Base { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.Inherits && h.TargetName == "Base");
    }

    // ── Interface / Implements ─────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ClassImplementsInterface_ReturnsImplementsHint()
    {
        var content = """
            namespace MyApp;
            public class MyService : IService { }
            public interface IService { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.Implements && h.TargetName == "IService");
    }

    [Fact]
    public async Task ParseAsync_InterfaceExtendsInterface_ReturnsImplementsHint()
    {
        var content = """
            namespace MyApp;
            public interface IChild : IParent { }
            public interface IParent { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.Implements && h.TargetName == "IParent");
    }

    // ── Field types ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ClassWithFieldOfCustomType_ReturnsContainsFieldHint()
    {
        var content = """
            namespace MyApp;
            public class Order {
                private Customer _customer;
            }
            public class Customer { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.ContainsFieldOfType && h.TargetName == "Customer");
    }

    [Fact]
    public async Task ParseAsync_FieldOfPrimitiveType_DoesNotReturnHint()
    {
        var content = """
            namespace MyApp;
            public class Counter {
                private int _count;
            }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.DoesNotContain(result.ReferenceHints,
            h => h.Kind == RelationshipKind.ContainsFieldOfType);
    }

    // ── Property types ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_PropertyOfCustomType_ReturnsContainsPropertyHint()
    {
        var content = """
            namespace MyApp;
            public class OrderLine {
                public Product Product { get; set; }
            }
            public class Product { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.ContainsPropertyOfType && h.TargetName == "Product");
    }

    // ── Method return type ─────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_MethodReturningCustomType_ReturnsTypeHint()
    {
        var content = """
            namespace MyApp;
            public class Factory {
                public Product Create() => default!;
            }
            public class Product { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.ReturnsType && h.TargetName == "Product");
    }

    [Fact]
    public async Task ParseAsync_MethodWithVoidReturn_DoesNotReturnHint()
    {
        var content = """
            namespace MyApp;
            public class Logger {
                public void Log(string message) { }
            }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.DoesNotContain(result.ReferenceHints,
            h => h.Kind == RelationshipKind.ReturnsType);
    }

    // ── Method parameter types ─────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_MethodAcceptingCustomParameter_ReturnsAcceptsParamHint()
    {
        var content = """
            namespace MyApp;
            public class OrderService {
                public void Process(Order order) { }
            }
            public class Order { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.AcceptsParameterType && h.TargetName == "Order");
    }

    // ── Using directives as imports ────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_UsingDirective_ProducesImportWithKindUsing()
    {
        var content = """
            using System.Collections.Generic;
            namespace MyApp;
            public class Foo { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.Imports,
            i => i.Kind == "using" && i.NormalizedTarget == "System.Collections.Generic");
    }

    [Fact]
    public async Task ParseAsync_UsingAliasDirective_ProducesImportWithKindUsingAlias()
    {
        var content = """
            using MyList = System.Collections.Generic.List<string>;
            namespace MyApp;
            public class Foo { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.Imports,
            i => i.Kind == "using-alias" && i.Alias == "MyList");
    }

    [Fact]
    public async Task ParseAsync_UsingStaticDirective_ProducesImportWithKindUsingStatic()
    {
        var content = """
            using static System.Math;
            namespace MyApp;
            public class Calc { }
            """;
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.Imports,
            i => i.Kind == "using-static" && i.NormalizedTarget == "System.Math");
    }

    // ── Capabilities ───────────────────────────────────────────────────────────

    [Fact]
    public void Capabilities_SupportsImportsAndReferences()
    {
        Assert.True(_adapter.Capabilities.SupportsImportsOrUses);
        Assert.True(_adapter.Capabilities.SupportsReferenceExtraction);
    }
}
