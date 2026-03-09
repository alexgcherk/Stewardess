// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using StewardessMCPService.CodeIndexing.Parsers.Python;
using Xunit;

namespace StewardessMCPService.CodeIndexing.Tests.Parsers;

public class PythonReferenceExtractionTests
{
    private readonly PythonParserAdapter _adapter = new();

    private static ParseRequest MakeRequest(string content)
    {
        return new ParseRequest
        {
            FileId = "py-ref",
            FilePath = "test.py",
            Content = content,
            LanguageId = "python",
            Mode = ParseMode.Declarations
        };
    }

    // ── Import extraction ──────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ImportStatement_ProducesImportEntry()
    {
        var content = "import os\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.Imports, i => i.Kind == "import" && i.NormalizedTarget == "os");
    }

    [Fact]
    public async Task ParseAsync_ImportWithAlias_PreservesAlias()
    {
        var content = "import numpy as np\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.Imports,
            i => i.Kind == "import" && i.NormalizedTarget == "numpy" && i.Alias == "np");
    }

    [Fact]
    public async Task ParseAsync_FromImport_ProducesFromImportEntry()
    {
        var content = "from collections import OrderedDict\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.Imports,
            i => i.Kind == "from-import" && i.NormalizedTarget == "collections");
    }

    [Fact]
    public async Task ParseAsync_RelativeImport_ProducesRelativeImportEntry()
    {
        var content = "from . import models\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.Imports, i => i.Kind == "relative-import");
    }

    [Fact]
    public async Task ParseAsync_MultipleImports_ProducesAllEntries()
    {
        var content = "import os\nimport sys\nfrom pathlib import Path\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Equal(3, result.Imports.Count);
    }

    // ── Base class reference hints ─────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ClassWithBaseClass_ReturnsInheritsHint()
    {
        var content = "class Dog(Animal):\n    pass\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.Inherits && h.TargetName == "Animal");
    }

    [Fact]
    public async Task ParseAsync_ClassWithMultipleBases_ReturnsMultipleHints()
    {
        var content = "class MyMixin(Base, Mixin):\n    pass\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.Inherits && h.TargetName == "Base");
        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.Inherits && h.TargetName == "Mixin");
    }

    [Fact]
    public async Task ParseAsync_ClassWithInterfaceLikeName_ReturnsImplementsHint()
    {
        // Python doesn't have interfaces, but a naming convention heuristic applies
        var content = "class MyService(IRepository):\n    pass\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Contains(result.ReferenceHints,
            h => h.Kind == RelationshipKind.Implements && h.TargetName == "IRepository");
    }

    [Fact]
    public async Task ParseAsync_ClassWithNoBase_ReturnsNoHints()
    {
        var content = "class StandaloneClass:\n    pass\n";
        var result = await _adapter.ParseAsync(MakeRequest(content));

        Assert.Empty(result.ReferenceHints);
    }

    // ── Capabilities ───────────────────────────────────────────────────────────

    [Fact]
    public void Capabilities_SupportsImportsAndReferences()
    {
        Assert.True(_adapter.Capabilities.SupportsImportsOrUses);
        Assert.True(_adapter.Capabilities.SupportsReferenceExtraction);
    }
}