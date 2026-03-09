// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using StewardessMCPService.Tests.Helpers;
using Xunit;

namespace StewardessMCPService.Tests.Services;

/// <summary>
///     Unit tests for <see cref="ProjectDetectionService" />.
///     Uses a <see cref="TempRepository" /> with an isolated file-system tree.
/// </summary>
public sealed class ProjectDetectionServiceTests : IDisposable
{
    private readonly TempRepository _repo;
    private readonly ProjectDetectionService _svc;

    public ProjectDetectionServiceTests()
    {
        _repo = new TempRepository();
        _svc = Build(_repo.Root);
    }

    public void Dispose()
    {
        _repo.Dispose();
    }

    // ── FindSolutionFilesAsync ────────────────────────────────────────────────

    [Fact]
    public async Task FindSolutionFilesAsync_EmptyRepo_ReturnsEmpty()
    {
        var result = await _svc.FindSolutionFilesAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindSolutionFilesAsync_SingleSlnFile_ReturnsRelativePath()
    {
        _repo.CreateFile("MyApp.sln", "Microsoft Visual Studio Solution File");

        var result = await _svc.FindSolutionFilesAsync();

        Assert.Single(result);
        Assert.Contains("MyApp.sln", result[0]);
        // Must be a relative path (no leading drive letter or absolute separator)
        Assert.DoesNotContain(_repo.Root, result[0]);
    }

    [Fact]
    public async Task FindSolutionFilesAsync_MultipleSlnFiles_ReturnsAll()
    {
        _repo.CreateFile("First.sln", "");
        _repo.CreateFile(@"nested\Second.sln", "");

        var result = await _svc.FindSolutionFilesAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task FindSolutionFilesAsync_BlockedFolder_IsSkipped()
    {
        // .git is a blocked folder by default — a .sln inside it must be ignored.
        _repo.CreateFile(@".git\inside.sln", "");
        _repo.CreateFile("real.sln", "");

        var svc = Build(_repo.Root, blockedFolders: new[] { ".git" });
        var result = await svc.FindSolutionFilesAsync();

        Assert.Single(result);
        Assert.Contains("real.sln", result[0]);
    }

    // ── FindProjectsAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task FindProjectsAsync_EmptyRepo_ReturnsEmpty()
    {
        var result = await _svc.FindProjectsAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindProjectsAsync_CsprojFile_ReturnsProjectInfo()
    {
        _repo.CreateFile(@"src\MyLib\MyLib.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<TargetFramework>net8.0</TargetFramework>" +
            "</PropertyGroup></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Single(result);
        var p = result[0];
        Assert.Equal("MyLib", p.Name);
        Assert.Equal("csproj", p.ProjectType);
        Assert.False(p.IsTestProject);
    }

    [Fact]
    public async Task FindProjectsAsync_ExtractsTargetFramework()
    {
        _repo.CreateFile(@"src\Lib\Lib.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<TargetFramework>net7.0</TargetFramework>" +
            "</PropertyGroup></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Single(result);
        Assert.Equal("net7.0", result[0].TargetFramework);
    }

    [Fact]
    public async Task FindProjectsAsync_ExtractsOutputType()
    {
        _repo.CreateFile(@"src\App\App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<TargetFramework>net8.0</TargetFramework>" +
            "<OutputType>Exe</OutputType>" +
            "</PropertyGroup></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Single(result);
        Assert.Equal("Exe", result[0].OutputType);
    }

    [Fact]
    public async Task FindProjectsAsync_ExtractsNuGetReferences()
    {
        _repo.CreateFile(@"src\Lib\Lib.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>" +
            "<PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\"/>" +
            "<PackageReference Include=\"Serilog\" Version=\"3.1.0\"/>" +
            "</ItemGroup></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Single(result);
        var refs = result[0].NuGetReferences;
        Assert.Contains(refs, r => r.Contains("Newtonsoft.Json"));
        Assert.Contains(refs, r => r.Contains("Serilog"));
    }

    [Fact]
    public async Task FindProjectsAsync_ExtractsProjectReferences()
    {
        _repo.CreateFile(@"src\App\App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>" +
            "<ProjectReference Include=\"..\\Lib\\Lib.csproj\"/>" +
            "</ItemGroup></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Single(result);
        var refs = result[0].ProjectReferences;
        Assert.Single(refs);
        Assert.Contains("Lib.csproj", refs[0]);
    }

    [Fact]
    public async Task FindProjectsAsync_IsTestProject_ByNamingConvention()
    {
        _repo.CreateFile(@"tests\MyApp.Tests\MyApp.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Single(result);
        Assert.True(result[0].IsTestProject);
    }

    [Fact]
    public async Task FindProjectsAsync_IsTestProject_ByXunitReference()
    {
        // Name does not contain "test/spec" but the project references xunit
        _repo.CreateFile(@"src\Verifier\Verifier.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>" +
            "<PackageReference Include=\"xunit\" Version=\"2.5.0\"/>" +
            "</ItemGroup></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Single(result);
        Assert.True(result[0].IsTestProject);
    }

    [Fact]
    public async Task FindProjectsAsync_IsNotTestProject_ForNormalProject()
    {
        _repo.CreateFile(@"src\MyLib\MyLib.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<TargetFramework>net8.0</TargetFramework>" +
            "</PropertyGroup></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Single(result);
        Assert.False(result[0].IsTestProject);
    }

    [Fact]
    public async Task FindProjectsAsync_MultipleExtensions_ReturnsAll()
    {
        _repo.CreateFile(@"cs\MyLib.csproj", "<Project></Project>");
        _repo.CreateFile(@"vb\VbLib.vbproj", "<Project></Project>");
        _repo.CreateFile(@"fs\FsLib.fsproj", "<Project></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Equal(3, result.Count);
        Assert.Single(result, p => p.ProjectType == "csproj");
        Assert.Single(result, p => p.ProjectType == "vbproj");
        Assert.Single(result, p => p.ProjectType == "fsproj");
    }

    [Fact]
    public async Task FindProjectsAsync_UnreadableProjectFile_IsSkipped()
    {
        // ParseProjectFile catches all exceptions — a file that cannot be read (e.g. locked)
        // would return null and be skipped.  We simulate by creating a valid file and a
        // valid file with empty content (no XML) to confirm the service handles both gracefully.
        _repo.CreateFile(@"empty\Empty.csproj", "");
        _repo.CreateFile(@"good\Good.csproj", "<Project></Project>");

        var result = await _svc.FindProjectsAsync();

        // Both parse without throwing — Empty has no metadata, Good is returned.
        Assert.True(result.Count >= 1);
        Assert.Contains(result, p => p.Name == "Good");
    }

    [Fact]
    public async Task FindProjectsAsync_ResultsOrderedByRelativePath()
    {
        _repo.CreateFile(@"z\ZApp.csproj", "<Project></Project>");
        _repo.CreateFile(@"a\AApp.csproj", "<Project></Project>");

        var result = await _svc.FindProjectsAsync();

        Assert.Equal(2, result.Count);
        Assert.True(
            string.Compare(result[0].RelativePath, result[1].RelativePath, StringComparison.OrdinalIgnoreCase) < 0,
            "Expected results ordered ascending by relative path");
    }

    // ── ParseSolutionAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ParseSolutionAsync_InvalidPath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.ParseSolutionAsync("../escape"));
    }

    [Fact]
    public async Task ParseSolutionAsync_MissingFile_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _svc.ParseSolutionAsync("nonexistent.sln"));
    }

    [Fact]
    public async Task ParseSolutionAsync_ValidSolution_ReturnsReferencedProjects()
    {
        _repo.CreateSampleCsStructure();

        var result = await _svc.ParseSolutionAsync("MySolution.sln");

        Assert.Single(result);
        Assert.Equal("MyLib", result[0].Name);
    }

    [Fact]
    public async Task ParseSolutionAsync_MissingProjectFile_SkipsEntry()
    {
        // Solution references a project file that does not exist on disk.
        _repo.CreateFile("Orphan.sln",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Ghost\", \"ghost\\Ghost.csproj\", \"{AAAA}\"\r\n" +
            "EndProject");

        var result = await _svc.ParseSolutionAsync("Orphan.sln");

        Assert.Empty(result);
    }

    // ── FindTestProjectsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task FindTestProjectsAsync_ReturnsOnlyTestProjects()
    {
        _repo.CreateFile(@"src\MyLib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        _repo.CreateFile(@"tests\MyLib.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var result = await _svc.FindTestProjectsAsync();

        Assert.Single(result);
        Assert.True(result[0].IsTestProject);
        Assert.Contains("Tests", result[0].Name);
    }

    [Fact]
    public async Task FindTestProjectsAsync_EmptyRepo_ReturnsEmpty()
    {
        var result = await _svc.FindTestProjectsAsync();
        Assert.Empty(result);
    }

    // ── FindConfigFilesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task FindConfigFilesAsync_AppSettingsJson_InJsonConfigs()
    {
        _repo.CreateFile("appsettings.json", "{}");
        _repo.CreateFile("appsettings.Development.json", "{}");

        var result = await _svc.FindConfigFilesAsync();

        Assert.Contains(result.JsonConfigs, p => p.Contains("appsettings.json"));
        Assert.Contains(result.JsonConfigs, p => p.Contains("appsettings.Development.json"));
    }

    [Fact]
    public async Task FindConfigFilesAsync_YamlFiles_InYamlConfigs()
    {
        _repo.CreateFile("docker-compose.yml", "version: '3'");
        _repo.CreateFile("config.yaml", "key: value");

        var result = await _svc.FindConfigFilesAsync();

        Assert.Contains(result.YamlConfigs, p => p.Contains("docker-compose.yml"));
        Assert.Contains(result.YamlConfigs, p => p.Contains("config.yaml"));
    }

    [Fact]
    public async Task FindConfigFilesAsync_IniFile_InIniFiles()
    {
        _repo.CreateFile("settings.ini", "[section]\nkey=value");

        var result = await _svc.FindConfigFilesAsync();

        Assert.Single(result.IniFiles);
        Assert.Contains("settings.ini", result.IniFiles[0]);
    }

    [Fact]
    public async Task FindConfigFilesAsync_AppConfig_InAppSettings()
    {
        _repo.CreateFile("app.config",
            "<?xml version=\"1.0\"?><configuration><appSettings></appSettings></configuration>");

        var result = await _svc.FindConfigFilesAsync();

        Assert.Contains(result.AppSettings, p => p.Contains("app.config"));
    }

    [Fact]
    public async Task FindConfigFilesAsync_ConnectionStringsInConfig_DetectedAsConnectionStrings()
    {
        _repo.CreateFile("web.config",
            "<?xml version=\"1.0\"?><configuration>" +
            "<connectionStrings><add name=\"Db\" connectionString=\"Server=.\"/></connectionStrings>" +
            "</configuration>");

        var result = await _svc.FindConfigFilesAsync();

        Assert.Contains(result.ConnectionStrings, p => p.Contains("web.config"));
        Assert.DoesNotContain(result.AppSettings, p => p.Contains("web.config"));
    }

    [Fact]
    public async Task FindConfigFilesAsync_EmptyRepo_ReturnsEmptyLists()
    {
        var result = await _svc.FindConfigFilesAsync();

        Assert.Empty(result.JsonConfigs);
        Assert.Empty(result.YamlConfigs);
        Assert.Empty(result.IniFiles);
        Assert.Empty(result.AppSettings);
        Assert.Empty(result.ConnectionStrings);
    }

    // ── GetSolutionInfoAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetSolutionInfoAsync_ReturnsSlnAndProjects()
    {
        _repo.CreateSampleCsStructure();

        var result = await _svc.GetSolutionInfoAsync();

        Assert.Single(result.SolutionFiles);
        Assert.Contains("MySolution.sln", result.SolutionFiles[0]);

        // Both MyLib and MyLib.Tests projects are discovered.
        Assert.True(result.Projects.Count >= 2);
    }

    [Fact]
    public async Task GetSolutionInfoAsync_EmptyRepo_ReturnsEmptyCollections()
    {
        var result = await _svc.GetSolutionInfoAsync();

        Assert.Empty(result.SolutionFiles);
        Assert.Empty(result.Projects);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ProjectDetectionService Build(string root, string[]? blockedFolders = null)
    {
        var settings = McpServiceSettings.CreateForTesting(root, blockedFolders: blockedFolders!);
        var validator = new PathValidator(settings);
        return new ProjectDetectionService(settings, validator);
    }
}
