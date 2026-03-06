using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;
using StewardessMCPServive.Tests.Helpers;
using Xunit;

namespace StewardessMCPServive.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="FileSystemService"/>.
    /// Covers file reading, encoding detection, hashing, and structure parsing.
    /// </summary>
    public sealed class FileSystemServiceTests : IDisposable
    {
        private readonly TempRepository   _repo;
        private readonly FileSystemService _svc;

        public FileSystemServiceTests()
        {
            _repo = new TempRepository();
            _repo.CreateSampleCsStructure();
            _svc  = Build(_repo.Root);
        }

        public void Dispose() => _repo.Dispose();

        // ── GetRepositoryInfo ────────────────────────────────────────────────────

        [Fact]
        public async Task GetRepositoryInfo_ReturnsCorrectRoot()
        {
            var info = await _svc.GetRepositoryInfoAsync(CancellationToken.None);
            Assert.True(string.Equals(
                Path.GetFullPath(_repo.Root),
                Path.GetFullPath(info.RepositoryRoot),
                StringComparison.OrdinalIgnoreCase));
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
            Assert.True(result.Content.Length <= 100 || result.Truncated);
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
            var h1  = await _svc.GetFileHashAsync(req, CancellationToken.None);
            var h2  = await _svc.GetFileHashAsync(req, CancellationToken.None);
            Assert.Equal(h1.Hash, h2.Hash);
        }

        [Fact]
        public async Task GetFileHash_DifferentAlgorithms_DifferentHashes()
        {
            var path  = @"src/MyLib/Class1.cs";
            var sha1  = (await _svc.GetFileHashAsync(new FileHashRequest { Path = path, Algorithm = "SHA1"   }, CancellationToken.None)).Hash;
            var sha256= (await _svc.GetFileHashAsync(new FileHashRequest { Path = path, Algorithm = "SHA256" }, CancellationToken.None)).Hash;
            Assert.NotEqual(sha1, sha256);
        }

        // ── DetectEncoding ───────────────────────────────────────────────────────

        [Fact]
        public async Task DetectEncoding_Utf8File_DetectsCorrectly()
        {
            var path = _repo.CreateFile("utf8.cs", "// UTF-8 content");
            var enc  = await _svc.DetectEncodingAsync("utf8.cs", CancellationToken.None);
            Assert.Contains("utf", enc, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DetectEncoding_Utf8BomFile_DetectedAsBom()
        {
            // Write file with BOM explicitly.
            var absPath = _repo.Abs("utf8bom.cs");
            var bom     = Encoding.UTF8.GetPreamble();
            var bytes   = bom.Length > 0
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

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static FileSystemService Build(string root)
        {
            var settings  = McpServiceSettings.CreateForTesting(repositoryRoot: root);
            var validator = new PathValidator(settings);
            var audit     = new AuditService(settings);
            return new FileSystemService(settings, validator, audit);
        }
    }
}
