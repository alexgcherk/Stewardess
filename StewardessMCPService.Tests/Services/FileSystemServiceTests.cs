// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Linq;
using System.Text;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using StewardessMCPService.Tests.Helpers;
using Xunit;

namespace StewardessMCPService.Tests.Services;

/// <summary>
///     Unit tests for <see cref="FileSystemService" />.
///     Covers file reading, encoding detection, hashing, and structure parsing.
/// </summary>
public sealed class FileSystemServiceTests : IDisposable
{
    private readonly TempRepository _repo;
    private readonly FileSystemService _svc;

    public FileSystemServiceTests()
    {
        _repo = new TempRepository();
        _repo.CreateSampleCsStructure();
        _svc = Build(_repo.Root);
    }

    public void Dispose()
    {
        _repo.Dispose();
    }

    // ── GetRepositoryInfo ────────────────────────────────────────────────────

    [Fact]
    public async Task GetRepositoryInfo_ReturnsCorrectRoot()
    {
        var info = await _svc.GetRepositoryInfoAsync(CancellationToken.None);
        Assert.Equal(
            Path.GetFullPath(_repo.Root),
            Path.GetFullPath(info.RepositoryRoot),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetRepositoryInfo_HasRepositoryName()
    {
        var info = await _svc.GetRepositoryInfoAsync(CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(info.RepositoryName));
    }

    // ── ListDirectory ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDirectory_Root_ReturnsEntries()
    {
        var result = await _svc.ListDirectoryAsync(
            new ListDirectoryRequest { Path = "" }, CancellationToken.None);
        Assert.NotEmpty(result.Entries);
    }

    [Fact]
    public async Task ListDirectory_SubPath_OnlyContainsChildEntries()
    {
        var result = await _svc.ListDirectoryAsync(
            new ListDirectoryRequest { Path = "src" }, CancellationToken.None);
        Assert.All(result.Entries, e => Assert.StartsWith("src", e.RelativePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListDirectory_NonExistentPath_Throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _svc.ListDirectoryAsync(new ListDirectoryRequest { Path = "doesnotexist" },
                CancellationToken.None));
    }

    // ── ReadFile ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadFile_ExistingFile_ReturnsContent()
    {
        var result = await _svc.ReadFileAsync(
            new ReadFileRequest { Path = @"src/MyLib/Class1.cs" }, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(result.Content));
        Assert.Contains("class Class1", result.Content);
    }

    [Fact]
    public async Task ReadFile_MissingFile_Throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _svc.ReadFileAsync(new ReadFileRequest { Path = "missing.cs" }, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFile_MaxBytesLimit_TruncatesLargeContent()
    {
        // Write a file larger than the limit.
        var bigContent = new string('x', 200);
        _repo.CreateFile("big.cs", bigContent);

        var result = await _svc.ReadFileAsync(
            new ReadFileRequest { Path = "big.cs", MaxBytes = 100 }, CancellationToken.None);
        Assert.True(result.Content!.Length <= 100 || result.Truncated);
    }

    // ── ReadFileRange ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadFileRange_ValidRange_ReturnsLines()
    {
        var result = await _svc.ReadFileRangeAsync(
            new ReadFileRangeRequest { Path = @"src/MyLib/Class1.cs", StartLine = 1, EndLine = 3 },
            CancellationToken.None);
        Assert.Equal(3, result.Lines.Count);
    }

    [Fact]
    public async Task ReadFileRange_EndMinusOne_ReturnsAllLines()
    {
        var result = await _svc.ReadFileRangeAsync(
            new ReadFileRangeRequest { Path = @"src/MyLib/Class1.cs", StartLine = 1, EndLine = -1 },
            CancellationToken.None);
        Assert.True(result.Lines.Count > 0);
    }

    // ── GetFileHash ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFileHash_SHA256_ReturnsSameHashTwice()
    {
        var req = new FileHashRequest { Path = @"src/MyLib/Class1.cs", Algorithm = "SHA256" };
        var h1 = await _svc.GetFileHashAsync(req, CancellationToken.None);
        var h2 = await _svc.GetFileHashAsync(req, CancellationToken.None);
        Assert.Equal(h1.Hash, h2.Hash);
    }

    [Fact]
    public async Task GetFileHash_DifferentAlgorithms_DifferentHashes()
    {
        var path = @"src/MyLib/Class1.cs";
        var sha1 = (await _svc.GetFileHashAsync(new FileHashRequest { Path = path, Algorithm = "SHA1" },
            CancellationToken.None)).Hash;
        var sha256 = (await _svc.GetFileHashAsync(new FileHashRequest { Path = path, Algorithm = "SHA256" },
            CancellationToken.None)).Hash;
        Assert.NotEqual(sha1, sha256);
    }

    // ── DetectEncoding ───────────────────────────────────────────────────────

    [Fact]
    public async Task DetectEncoding_Utf8File_DetectsCorrectly()
    {
        var path = _repo.CreateFile("utf8.cs", "// UTF-8 content");
        var enc = await _svc.DetectEncodingAsync("utf8.cs", CancellationToken.None);
        Assert.Contains("utf", enc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectEncoding_Utf8BomFile_DetectedAsBom()
    {
        // Write file with BOM explicitly.
        var absPath = _repo.Abs("utf8bom.cs");
        var bom = Encoding.UTF8.GetPreamble();
        var bytes = bom.Length > 0
            ? new byte[bom.Length + 10]
            : new byte[10];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        File.WriteAllBytes(absPath, bytes);

        var enc = await _svc.DetectEncodingAsync("utf8bom.cs", CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(enc));
    }

    // ── GetFileStructure ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetFileStructure_CsFile_DetectsNamespaceAndClass()
    {
        var result = await _svc.GetFileStructureSummaryAsync(
            new FileStructureSummaryRequest { Path = @"src/MyLib/Class1.cs" },
            CancellationToken.None);
        Assert.True(result.Namespaces.Count > 0 || result.TopLevelTypes.Count > 0,
            "Expected at least one namespace or type");
    }

    // ── PathExists ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PathExists_ExistingFile_ReturnsTrue()
    {
        var result = await _svc.PathExistsAsync(@"src/MyLib/Class1.cs", CancellationToken.None);
        Assert.True(result.Exists);
        Assert.Equal("file", result.Type);
    }

    [Fact]
    public async Task PathExists_ExistingDirectory_ReturnsTrue()
    {
        var result = await _svc.PathExistsAsync("src", CancellationToken.None);
        Assert.True(result.Exists);
        Assert.Equal("directory", result.Type);
    }

    [Fact]
    public async Task PathExists_MissingPath_ReturnsFalse()
    {
        var result = await _svc.PathExistsAsync("totally_missing.xyz", CancellationToken.None);
        Assert.False(result.Exists);
    }

    // ── ListTree ─────────────────────────────────────────────────────────────

    /// <summary>
    ///     Regression test: path="." resolves to the repository root, whose absolute path
    ///     is one character shorter than _repositoryRoot (which has a trailing separator).
    ///     Previously this caused "startIndex cannot be larger than length of string".
    /// </summary>
    [Fact]
    public async Task ListTree_DotPath_DoesNotThrow()
    {
        var result = await _svc.ListTreeAsync(
            new ListTreeRequest { Path = ".", MaxDepth = 1 }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Root);
        Assert.True(result.TotalDirectories >= 0);
    }

    /// <summary>
    ///     Regression test: path="" (empty) also maps to the repository root and must
    ///     not throw the startIndex error.
    /// </summary>
    [Fact]
    public async Task ListTree_EmptyPath_DoesNotThrow()
    {
        var result = await _svc.ListTreeAsync(
            new ListTreeRequest { Path = "", MaxDepth = 1 }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Root);
    }

    /// <summary>
    ///     Regression test: maxDepth=-1 (unbounded) previously caused Math.Min(-1, limit) = -1,
    ///     making currentDepth (0) >= maxDepth (-1) immediately true so no directory was expanded.
    /// </summary>
    [Fact]
    public async Task ListTree_NegativeMaxDepth_ExpandsTree()
    {
        // Structure has at least src/ and tests/ directories.
        var result = await _svc.ListTreeAsync(
            new ListTreeRequest { Path = "", MaxDepth = -1 }, CancellationToken.None);

        Assert.NotNull(result.Root);
        Assert.NotEmpty(result.Root.Children!);
        Assert.True(result.TotalDirectories > 0, "Expected directories to be enumerated");
    }

    [Fact]
    public async Task ListTree_MaxDepthZero_ReturnsOnlyRootWithNoChildren()
    {
        var result = await _svc.ListTreeAsync(
            new ListTreeRequest { Path = "", MaxDepth = 0 }, CancellationToken.None);

        Assert.NotNull(result.Root);
        Assert.Empty(result.Root.Children ?? new List<TreeNode>());
    }

    [Fact]
    public async Task ListTree_MaxDepthOne_ReturnsOnlyTopLevelChildren()
    {
        var result = await _svc.ListTreeAsync(
            new ListTreeRequest { Path = "", MaxDepth = 1 }, CancellationToken.None);

        Assert.NotNull(result.Root);
        Assert.NotEmpty(result.Root.Children!);
        // No grandchildren should be present at depth 1.
        Assert.All(result.Root.Children!, child =>
            Assert.Empty(child.Children ?? new List<TreeNode>()));
    }

    [Fact]
    public async Task ListTree_DirectoriesOnly_ContainsNoFileNodes()
    {
        var result = await _svc.ListTreeAsync(
            new ListTreeRequest { Path = "", MaxDepth = 3, DirectoriesOnly = true },
            CancellationToken.None);

        // Walk all nodes and assert none are files.
        AssertNoFileNodes(result.Root);
    }

    [Fact]
    public async Task ListTree_SubDirectory_RootNodeHasCorrectRelativePath()
    {
        var result = await _svc.ListTreeAsync(
            new ListTreeRequest { Path = "src", MaxDepth = 1 }, CancellationToken.None);

        Assert.Equal("src", result.Path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListTree_RepositoryRoot_RelativePathIsEmpty()
    {
        var result = await _svc.ListTreeAsync(
            new ListTreeRequest { Path = "", MaxDepth = 1 }, CancellationToken.None);

        // The root node's relative path should be empty (it IS the root).
        Assert.Equal(string.Empty, result.Root.RelativePath);
    }

    // ── GetMetadata ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetadata_ExistingFile_ReturnsCorrectInfo()
    {
        var content = "hello metadata";
        _repo.CreateFile("meta.txt", content);

        var result = await _svc.GetMetadataAsync(
            new FileMetadataRequest { Path = "meta.txt" }, CancellationToken.None);

        Assert.Equal("meta.txt", result.RelativePath);
        Assert.True(result.SizeBytes > 0);
        Assert.NotEqual(default, result.LastModified);
    }

    [Fact]
    public async Task GetMetadata_MissingFile_Throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _svc.GetMetadataAsync(
                new FileMetadataRequest { Path = "does_not_exist.txt" }, CancellationToken.None));
    }

    // ── ReadMultipleFiles ────────────────────────────────────────────────────

    [Fact]
    public async Task ReadMultipleFiles_TwoFiles_ReturnsBoth()
    {
        _repo.CreateFile("a.txt", "content-a");
        _repo.CreateFile("b.txt", "content-b");

        var result = await _svc.ReadMultipleFilesAsync(
            new ReadMultipleFilesRequest { Paths = new List<string> { "a.txt", "b.txt" } },
            CancellationToken.None);

        Assert.Equal(2, result.Files.Count);
        var paths = result.Files.Select(f => f.RelativePath).ToList();
        Assert.Contains(paths, p => p.Contains("a.txt"));
        Assert.Contains(paths, p => p.Contains("b.txt"));
    }

    [Fact]
    public async Task ReadMultipleFiles_MissingFileIncluded_OtherFilesStillReturned()
    {
        _repo.CreateFile("good.txt", "content");

        var result = await _svc.ReadMultipleFilesAsync(
            new ReadMultipleFilesRequest { Paths = new List<string> { "good.txt", "missing.txt" } },
            CancellationToken.None);

        // The good file should be returned; the missing one is either omitted or marked as error.
        Assert.Contains(result.Files, f => f.RelativePath != null && f.RelativePath.Contains("good.txt"));
    }

    // ── DetectLineEnding ─────────────────────────────────────────────────────

    [Fact]
    public async Task DetectLineEnding_CrLfFile_DetectedAsCrLf()
    {
        var absPath = _repo.Abs("crlf.txt");
        File.WriteAllBytes(absPath, System.Text.Encoding.UTF8.GetBytes("line1\r\nline2\r\n"));

        var result = await _svc.DetectLineEndingAsync("crlf.txt", CancellationToken.None);

        Assert.Contains("CRLF", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectLineEnding_LfFile_DetectedAsLf()
    {
        var absPath = _repo.Abs("lf.txt");
        File.WriteAllBytes(absPath, System.Text.Encoding.UTF8.GetBytes("line1\nline2\n"));

        var result = await _svc.DetectLineEndingAsync("lf.txt", CancellationToken.None);

        Assert.Contains("LF", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── GetFileHash – additional algorithms ──────────────────────────────────

    [Fact]
    public async Task GetFileHash_MD5_ReturnsNonEmptyHash()
    {
        var req = new FileHashRequest { Path = @"src/MyLib/Class1.cs", Algorithm = "MD5" };
        var result = await _svc.GetFileHashAsync(req, CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.Hash));
        Assert.Equal("MD5", result.Algorithm, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetFileHash_SHA1_ReturnsNonEmptyHash()
    {
        var req = new FileHashRequest { Path = @"src/MyLib/Class1.cs", Algorithm = "SHA1" };
        var result = await _svc.GetFileHashAsync(req, CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.Hash));
    }

    // ── ListDirectory – name pattern filtering ───────────────────────────────

    [Fact]
    public async Task ListDirectory_WithNamePattern_FiltersByWildcard()
    {
        // CreateSampleCsStructure has src/ and tests/ directories in root.
        // Filter root entries to only those matching "src"
        var result = await _svc.ListDirectoryAsync(
            new ListDirectoryRequest { Path = "", NamePattern = "src" }, CancellationToken.None);

        Assert.All(result.Entries, e =>
            Assert.Equal("src", e.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListDirectory_WithCsPattern_ReturnsOnlyCsFiles()
    {
        var result = await _svc.ListDirectoryAsync(
            new ListDirectoryRequest { Path = @"src/MyLib", NamePattern = "*.cs" },
            CancellationToken.None);

        Assert.NotEmpty(result.Entries);
        Assert.All(result.Entries, e =>
            Assert.EndsWith(".cs", e.Name, StringComparison.OrdinalIgnoreCase));
    }

    // ── GetRepositoryInfo – git detection ────────────────────────────────────

    [Fact]
    public async Task GetRepositoryInfo_WithDotGitDirectory_ShowsIsGitRepository()
    {
        Directory.CreateDirectory(Path.Combine(_repo.Root, ".git"));

        var info = await _svc.GetRepositoryInfoAsync(CancellationToken.None);

        Assert.NotNull(info.GitInfo);
        Assert.True(info.GitInfo.IsGitRepository);
    }

    [Fact]
    public async Task GetRepositoryInfo_WithoutDotGitDirectory_IsGitRepositoryFalse()
    {
        var info = await _svc.GetRepositoryInfoAsync(CancellationToken.None);

        Assert.NotNull(info.GitInfo);
        Assert.False(info.GitInfo.IsGitRepository);
    }

    // ── ReadFile – Head / Tail ────────────────────────────────────────────────

    [Fact]
    public async Task ReadFile_Head_ReturnsOnlyFirstNLines()
    {
        _repo.CreateFile("multiline.txt", "line1\nline2\nline3\nline4\nline5");

        var result = await _svc.ReadFileAsync(
            new ReadFileRequest { Path = "multiline.txt", Head = 2 }, CancellationToken.None);

        var lines = result.Content!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= 2, $"Expected at most 2 lines, got {lines.Length}");
        Assert.StartsWith("line1", lines[0]);
    }

    [Fact]
    public async Task ReadFile_Tail_ReturnsOnlyLastNLines()
    {
        _repo.CreateFile("multiline2.txt", "line1\nline2\nline3\nline4\nline5");

        var result = await _svc.ReadFileAsync(
            new ReadFileRequest { Path = "multiline2.txt", Tail = 2 }, CancellationToken.None);

        var lines = result.Content!.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= 2, $"Expected at most 2 lines, got {lines.Length}");
    }

    // ── ReadMediaFileAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ReadMediaFileAsync_ExistingFile_ReturnsBase64Content()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        File.WriteAllBytes(_repo.Abs("icon.png"), bytes);

        var response = await _svc.ReadMediaFileAsync(
            new ReadMediaFileRequest { Path = "icon.png" }, CancellationToken.None);

        Assert.Equal(Convert.ToBase64String(bytes), response.ContentBase64);
        Assert.Equal(bytes.Length, response.SizeBytes);
    }

    [Fact]
    public async Task ReadMediaFileAsync_PngExtension_ReturnsPngMimeType()
    {
        File.WriteAllBytes(_repo.Abs("image.png"), new byte[] { 0x00, 0x01 });

        var response = await _svc.ReadMediaFileAsync(
            new ReadMediaFileRequest { Path = "image.png" }, CancellationToken.None);

        Assert.Equal("image/png", response.MimeType);
    }

    [Fact]
    public async Task ReadMediaFileAsync_JpegExtension_ReturnsJpegMimeType()
    {
        File.WriteAllBytes(_repo.Abs("photo.jpg"), new byte[] { 0xFF, 0xD8 });

        var response = await _svc.ReadMediaFileAsync(
            new ReadMediaFileRequest { Path = "photo.jpg" }, CancellationToken.None);

        Assert.Equal("image/jpeg", response.MimeType);
    }

    [Fact]
    public async Task ReadMediaFileAsync_PdfExtension_ReturnsPdfMimeType()
    {
        File.WriteAllBytes(_repo.Abs("doc.pdf"), new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var response = await _svc.ReadMediaFileAsync(
            new ReadMediaFileRequest { Path = "doc.pdf" }, CancellationToken.None);

        Assert.Equal("application/pdf", response.MimeType);
    }

    [Fact]
    public async Task ReadMediaFileAsync_UnknownExtension_ReturnsOctetStream()
    {
        File.WriteAllBytes(_repo.Abs("file.xyz"), new byte[] { 0x01, 0x02 });

        var response = await _svc.ReadMediaFileAsync(
            new ReadMediaFileRequest { Path = "file.xyz" }, CancellationToken.None);

        Assert.Equal("application/octet-stream", response.MimeType);
    }

    [Fact]
    public async Task ReadMediaFileAsync_MissingFile_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _svc.ReadMediaFileAsync(
                new ReadMediaFileRequest { Path = "no-such.png" }, CancellationToken.None));
    }

    // ── ReadFileRangeAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ReadFileRangeAsync_StartLineExceedsFileLength_ThrowsArgumentOutOfRange()
    {
        _repo.CreateFile("short.txt", "line1\nline2");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _svc.ReadFileRangeAsync(
                new ReadFileRangeRequest { Path = "short.txt", StartLine = 999 },
                CancellationToken.None));
    }

    [Fact]
    public async Task ReadFileRangeAsync_WithIncludeLineNumbers_ContentContainsLineNumbers()
    {
        _repo.CreateFile("numbered.txt", "alpha\nbeta\ngamma");

        var response = await _svc.ReadFileRangeAsync(
            new ReadFileRangeRequest { Path = "numbered.txt", StartLine = 2, EndLine = 3, IncludeLineNumbers = true },
            CancellationToken.None);

        Assert.Contains("2", response.Content);
        Assert.Contains("beta", response.Content);
        Assert.Equal(2, response.StartLine);
        Assert.Equal(3, response.EndLine);
    }

    [Fact]
    public async Task ReadFileRangeAsync_SubsetOfLines_ReturnsOnlyRequestedRange()
    {
        _repo.CreateFile("range.txt", "A\nB\nC\nD\nE");

        var response = await _svc.ReadFileRangeAsync(
            new ReadFileRangeRequest { Path = "range.txt", StartLine = 2, EndLine = 4 },
            CancellationToken.None);

        Assert.Equal(2, response.StartLine);
        Assert.Equal(4, response.EndLine);
        Assert.DoesNotContain("A", response.Content);
        Assert.Contains("B", response.Content);
        Assert.Contains("D", response.Content);
        Assert.DoesNotContain("E", response.Content);
    }

    // ── GetFileHash – unknown algorithm defaults to SHA256 ───────────────────

    [Fact]
    public async Task GetFileHash_UnknownAlgorithm_DefaultsToSha256()
    {
        _repo.CreateFile("hashme.txt", "Hello");

        var response = await _svc.GetFileHashAsync(
            new FileHashRequest { Path = "hashme.txt", Algorithm = "BLAKE2" },
            CancellationToken.None);

        Assert.Equal("SHA256", response.Algorithm);
        Assert.NotEmpty(response.Hash);
    }

    // ── GetFileStructureSummaryAsync (ParseCodeStructure) ────────────────────

    [Fact]
    public async Task GetFileStructureSummaryAsync_CsFile_ExtractsNamespaceAndType()
    {
        const string code = @"using System;
namespace MyApp.Domain
{
    public class OrderService
    {
        public void Process() {}
    }
}";
        _repo.CreateFile("OrderService.cs", code);

        var response = await _svc.GetFileStructureSummaryAsync(
            new FileStructureSummaryRequest { Path = "OrderService.cs" },
            CancellationToken.None);

        Assert.Equal("csharp", response.Language);
        Assert.Contains("System", response.UsingDirectives);
        Assert.Single(response.Namespaces);
        Assert.Equal("MyApp.Domain", response.Namespaces[0].Name);
        Assert.Contains(response.Namespaces[0].Types, t => t.Name == "OrderService");
    }

    [Fact]
    public async Task GetFileStructureSummaryAsync_ExtractsMethodMembers()
    {
        const string code = @"namespace App
{
    public class Calc
    {
        public int Add(int a, int b) { return a + b; }
        private void Helper() {}
    }
}";
        _repo.CreateFile("Calc.cs", code);

        var response = await _svc.GetFileStructureSummaryAsync(
            new FileStructureSummaryRequest { Path = "Calc.cs" },
            CancellationToken.None);

        var calc = response.Namespaces[0].Types.First(t => t.Name == "Calc");
        Assert.NotEmpty(calc.Members);
        Assert.Contains(calc.Members, m => m.Name == "Add" && m.IsPublic);
    }

    [Fact]
    public async Task GetFileStructureSummaryAsync_TypeScriptFile_MapsLanguage()
    {
        _repo.CreateFile("service.ts", "export class AppService {}");

        var response = await _svc.GetFileStructureSummaryAsync(
            new FileStructureSummaryRequest { Path = "service.ts" },
            CancellationToken.None);

        Assert.Equal("typescript", response.Language);
    }

    [Fact]
    public async Task GetFileStructureSummaryAsync_PythonFile_MapsLanguage()
    {
        _repo.CreateFile("main.py", "def main(): pass");

        var response = await _svc.GetFileStructureSummaryAsync(
            new FileStructureSummaryRequest { Path = "main.py" },
            CancellationToken.None);

        Assert.Equal("python", response.Language);
    }

    [Fact]
    public async Task GetFileStructureSummaryAsync_UnknownExtension_ReturnsTextLanguage()
    {
        _repo.CreateFile("data.xyz", "some content");

        var response = await _svc.GetFileStructureSummaryAsync(
            new FileStructureSummaryRequest { Path = "data.xyz" },
            CancellationToken.None);

        Assert.Equal("text", response.Language);
    }

    [Fact]
    public async Task GetFileStructureSummaryAsync_MissingFile_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _svc.GetFileStructureSummaryAsync(
                new FileStructureSummaryRequest { Path = "nope.cs" },
                CancellationToken.None));
    }

    // ── IsBinaryFile (internal static) ───────────────────────────────────────

    [Fact]
    public void IsBinaryFile_KnownBinaryExtension_ReturnsTrue()
    {
        // We use a known binary extension — the file doesn't even need to exist
        // because the extension check happens first.
        var absPath = _repo.Abs("image.png");
        File.WriteAllBytes(absPath, new byte[] { 1, 2, 3 });

        Assert.True(FileSystemService.IsBinaryFile(absPath));
    }

    [Fact]
    public void IsBinaryFile_TextFileNoBinaryExtension_ReturnsFalse()
    {
        _repo.CreateFile("readme.txt", "Hello World");

        Assert.False(FileSystemService.IsBinaryFile(_repo.Abs("readme.txt")));
    }

    [Fact]
    public void IsBinaryFile_FileContainingNullByte_ReturnsTrue()
    {
        var absPath = _repo.Abs("with_null.dat2");
        var bytes = new byte[] { (byte)'H', (byte)'i', 0x00, (byte)'!' };
        File.WriteAllBytes(absPath, bytes);

        Assert.True(FileSystemService.IsBinaryFile(absPath));
    }

    // ── DetectFileEncodingSync (internal static) ─────────────────────────────

    [Fact]
    public void DetectFileEncodingSync_Utf8Bom_ReturnsUtf8Bom()
    {
        var absPath = _repo.Abs("bom_utf8.txt");
        // Write UTF-8 BOM (EF BB BF) followed by some text.
        var content = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'H', (byte)'i' };
        File.WriteAllBytes(absPath, content);

        Assert.Equal("utf-8-bom", FileSystemService.DetectFileEncodingSync(absPath));
    }

    [Fact]
    public void DetectFileEncodingSync_Utf16LeBom_ReturnsUtf16Le()
    {
        var absPath = _repo.Abs("bom_utf16le.txt");
        var content = new byte[] { 0xFF, 0xFE, (byte)'H', 0x00 };
        File.WriteAllBytes(absPath, content);

        Assert.Equal("utf-16-le", FileSystemService.DetectFileEncodingSync(absPath));
    }

    [Fact]
    public void DetectFileEncodingSync_Utf16BeBom_ReturnsUtf16Be()
    {
        var absPath = _repo.Abs("bom_utf16be.txt");
        var content = new byte[] { 0xFE, 0xFF, 0x00, (byte)'H' };
        File.WriteAllBytes(absPath, content);

        Assert.Equal("utf-16-be", FileSystemService.DetectFileEncodingSync(absPath));
    }

    [Fact]
    public void DetectFileEncodingSync_NoBom_ReturnsUtf8()
    {
        _repo.CreateFile("nobom.txt", "plain text with no BOM");

        Assert.Equal("utf-8", FileSystemService.DetectFileEncodingSync(_repo.Abs("nobom.txt")));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AssertNoFileNodes(TreeNode node)
    {
        if (node == null) return;
        foreach (var child in node.Children ?? new List<TreeNode>())
        {
            Assert.NotEqual("file", child.Type, StringComparer.OrdinalIgnoreCase);
            AssertNoFileNodes(child);
        }
    }

    private static FileSystemService Build(string root)
    {
        var settings = McpServiceSettings.CreateForTesting(root);
        var validator = new PathValidator(settings);
        var audit = new AuditService(settings);
        return new FileSystemService(settings, validator, audit);
    }
}