// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.LanguageDetection;
using Xunit;

namespace StewardessMCPService.CodeIndexing.Tests.LanguageDetection;

public class DefaultLanguageDetectorTests
{
    private readonly DefaultLanguageDetector _detector = new();

    [Theory]
    [InlineData("Program.cs",       "csharp")]
    [InlineData("Module.vb",        "vbnet")]
    [InlineData("main.py",          "python")]
    [InlineData("app.ts",           "typescript")]
    [InlineData("index.js",         "javascript")]
    [InlineData("Main.java",        "java")]
    [InlineData("main.go",          "go")]
    [InlineData("lib.rs",           "rust")]
    [InlineData("service.cpp",      "cpp")]
    [InlineData("header.h",         "c")]
    [InlineData("schema.json",      "json")]
    [InlineData("config.xml",       "xml")]
    [InlineData("index.html",       "html")]
    [InlineData("styles.css",       "css")]
    [InlineData("query.sql",        "sql")]
    [InlineData("build.sh",         "shell")]
    [InlineData("deploy.ps1",       "powershell")]
    [InlineData("README.md",        "markdown")]
    public void Detect_KnownExtension_ReturnsCorrectLanguage(string fileName, string expectedLang)
    {
        var result = _detector.Detect(fileName);

        Assert.True(result.IsKnown);
        Assert.Equal(expectedLang, result.LanguageId);
    }

    [Fact]
    public void Detect_UnknownExtension_ReturnsUnknown()
    {
        var result = _detector.Detect("file.xyz123");
        Assert.Equal(CodeIndexing.LanguageDetection.LanguageId.Unknown, result.LanguageId);
    }

    [Fact]
    public void Detect_ShebangPython_ReturnsPython()
    {
        var result = _detector.Detect("script", "#!/usr/bin/env python3\nprint('hi')");
        Assert.Equal(CodeIndexing.LanguageDetection.LanguageId.Python, result.LanguageId);
        Assert.Equal("shebang", result.DetectionMethod);
    }

    [Fact]
    public void Detect_ShebangBash_ReturnsShell()
    {
        var result = _detector.Detect("myscript", "#!/bin/bash\necho hello");
        Assert.Equal(CodeIndexing.LanguageDetection.LanguageId.Shell, result.LanguageId);
    }

    [Fact]
    public void Detect_CSharpFile_HasHighConfidence()
    {
        var result = _detector.Detect("Foo.cs");
        Assert.True(result.Confidence >= 0.9);
    }

    [Fact]
    public void Detect_NullPath_DoesNotThrow()
    {
        // Should return Unknown gracefully, not throw
        var result = _detector.Detect("");
        Assert.Equal(CodeIndexing.LanguageDetection.LanguageId.Unknown, result.LanguageId);
    }
}
