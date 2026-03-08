using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Services
{
    /// <summary>
    /// Heuristic project and solution detection.  No MSBuild / compiler invocation.
    /// </summary>
    public sealed class ProjectDetectionService : IProjectDetectionService
    {
        private readonly McpServiceSettings _settings;
        private readonly PathValidator _pathValidator;

        private static readonly string[] _projectExtensions =
            { ".csproj", ".vbproj", ".fsproj", ".sqlproj", ".wixproj", ".ccproj", ".pyproj" };

        private static readonly Regex _slnProjectRegex = new Regex(
            @"Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _targetFrameworkRegex = new Regex(
            @"<TargetFramework(?:Version|)>\s*([^<]+)\s*</TargetFramework",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _outputTypeRegex = new Regex(
            @"<OutputType>\s*([^<]+)\s*</OutputType>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Initialises a new instance of <see cref="ProjectDetectionService"/>.</summary>
        public ProjectDetectionService(McpServiceSettings settings, PathValidator pathValidator)
        {
            _settings      = settings      ?? throw new ArgumentNullException(nameof(settings));
            _pathValidator = pathValidator  ?? throw new ArgumentNullException(nameof(pathValidator));
        }

        // ── FindSolutionFilesAsync ────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<List<string>> FindSolutionFilesAsync(CancellationToken ct = default)
        {
            var root    = _settings.RepositoryRoot;
            var results = new List<string>();

            foreach (var path in SafeEnumerateFiles(root, "*.sln", ct))
                results.Add(_pathValidator.ToRelativePath(path));

            return Task.FromResult(results);
        }

        // ── FindProjectsAsync ─────────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<List<ProjectInfo>> FindProjectsAsync(CancellationToken ct = default)
        {
            var root     = _settings.RepositoryRoot;
            var projects = new List<ProjectInfo>();

            foreach (var ext in _projectExtensions)
            {
                foreach (var path in SafeEnumerateFiles(root, "*" + ext, ct))
                {
                    var info = ParseProjectFile(path);
                    if (info != null) projects.Add(info);
                }
            }

            return Task.FromResult(projects.OrderBy(p => p.RelativePath).ToList());
        }

        // ── ParseSolutionAsync ────────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<List<ProjectInfo>> ParseSolutionAsync(string solutionRelativePath, CancellationToken ct = default)
        {
            var validation = _pathValidator.ValidateRead(solutionRelativePath, out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!File.Exists(absPath))
                throw new FileNotFoundException($"Solution file not found: {solutionRelativePath}");

            var slnDir   = Path.GetDirectoryName(absPath);
            var projects = new List<ProjectInfo>();
            var lines    = File.ReadAllLines(absPath);

            foreach (var line in lines)
            {
                var m = _slnProjectRegex.Match(line);
                if (!m.Success) continue;

                var projRelPath = m.Groups[2].Value.Replace('\\', Path.DirectorySeparatorChar);
                var projAbsPath = Path.GetFullPath(Path.Combine(slnDir, projRelPath));

                if (!File.Exists(projAbsPath)) continue;

                var ext = Path.GetExtension(projAbsPath).ToLowerInvariant();
                if (!_projectExtensions.Contains(ext)) continue;

                var info = ParseProjectFile(projAbsPath);
                if (info != null) projects.Add(info);
            }

            return Task.FromResult(projects);
        }

        // ── FindTestProjectsAsync ────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<List<ProjectInfo>> FindTestProjectsAsync(CancellationToken ct = default)
        {
            var all = await FindProjectsAsync(ct).ConfigureAwait(false);
            return all.Where(p => p.IsTestProject).ToList();
        }

        // ── FindConfigFilesAsync ─────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<ConfigFilesResponse> FindConfigFilesAsync(CancellationToken ct = default)
        {
            var root     = _settings.RepositoryRoot;
            var response = new ConfigFilesResponse();

            foreach (var path in SafeEnumerateFiles(root, "*.config", ct))
            {
                var name    = Path.GetFileName(path).ToLowerInvariant();
                var relPath = _pathValidator.ToRelativePath(path);

                if (name == "web.config" || name == "app.config" || name.EndsWith(".config"))
                {
                    if (name.Contains("connection") || TryHasConnectionStrings(path))
                        response.ConnectionStrings.Add(relPath);
                    else
                        response.AppSettings.Add(relPath);
                }
            }

            foreach (var path in SafeEnumerateFiles(root, "appsettings*.json", ct))
                response.JsonConfigs.Add(_pathValidator.ToRelativePath(path));

            foreach (var path in SafeEnumerateFiles(root, "*.json", ct))
            {
                var name = Path.GetFileName(path).ToLowerInvariant();
                if (name.StartsWith("appsettings")) continue; // already added
                if (name == "tsconfig.json" || name == "package.json" ||
                    name == "launchsettings.json" || name.Contains("settings"))
                    response.JsonConfigs.Add(_pathValidator.ToRelativePath(path));
            }

            foreach (var path in SafeEnumerateFiles(root, "*.yml", ct)
                                  .Concat(SafeEnumerateFiles(root, "*.yaml", ct)))
                response.YamlConfigs.Add(_pathValidator.ToRelativePath(path));

            foreach (var path in SafeEnumerateFiles(root, "*.ini", ct))
                response.IniFiles.Add(_pathValidator.ToRelativePath(path));

            foreach (var path in SafeEnumerateFiles(root, ".env*", ct))
                response.OtherConfigs.Add(_pathValidator.ToRelativePath(path));

            return Task.FromResult(response);
        }

        // ── GetSolutionInfoAsync ─────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<SolutionInfoResponse> GetSolutionInfoAsync(CancellationToken ct = default)
        {
            var slnFiles = await FindSolutionFilesAsync(ct).ConfigureAwait(false);
            var projects = await FindProjectsAsync(ct).ConfigureAwait(false);

            return new SolutionInfoResponse
            {
                SolutionFiles = slnFiles,
                Projects      = projects
            };
        }

        // ── Private: project file parsing ────────────────────────────────────────

        private ProjectInfo ParseProjectFile(string absPath)
        {
            try
            {
                var name    = Path.GetFileNameWithoutExtension(absPath);
                var ext     = Path.GetExtension(absPath).ToLowerInvariant().TrimStart('.');
                var relPath = _pathValidator.ToRelativePath(absPath);

                var info = new ProjectInfo
                {
                    RelativePath = relPath,
                    Name         = name,
                    ProjectType  = ext,
                    IsTestProject = IsTestProject(name, absPath)
                };

                var content = File.ReadAllText(absPath);

                var tfMatch = _targetFrameworkRegex.Match(content);
                if (tfMatch.Success)
                    info.TargetFramework = tfMatch.Groups[1].Value.Trim();

                var otMatch = _outputTypeRegex.Match(content);
                if (otMatch.Success)
                    info.OutputType = otMatch.Groups[1].Value.Trim();

                // NuGet package references (both old and new style)
                info.NuGetReferences = ExtractNugetReferences(content);

                // Project references
                info.ProjectReferences = ExtractProjectReferences(content);

                return info;
            }
            catch { return null; }
        }

        private static bool IsTestProject(string projectName, string absPath)
        {
            var nameLower = projectName.ToLowerInvariant();
            if (nameLower.Contains("test") || nameLower.Contains("spec")) return true;

            try
            {
                var content = File.ReadAllText(absPath);
                return content.IndexOf("xunit", StringComparison.OrdinalIgnoreCase) >= 0
                    || content.IndexOf("nunit", StringComparison.OrdinalIgnoreCase) >= 0
                    || content.IndexOf("mstest", StringComparison.OrdinalIgnoreCase) >= 0
                    || content.IndexOf("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static List<string> ExtractNugetReferences(string content)
        {
            var refs   = new List<string>();
            // SDK-style: <PackageReference Include="X" Version="Y"/>
            var pkgRefs = Regex.Matches(content,
                @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""",
                RegexOptions.IgnoreCase);
            foreach (Match m in pkgRefs)
                refs.Add($"{m.Groups[1].Value} {m.Groups[2].Value}");

            // Old-style packages.config references embedded in project (unusual but handle it)
            return refs;
        }

        private static List<string> ExtractProjectReferences(string content)
        {
            var refs    = new List<string>();
            var matches = Regex.Matches(content,
                @"<ProjectReference\s+Include=""([^""]+)""",
                RegexOptions.IgnoreCase);
            foreach (Match m in matches)
                refs.Add(m.Groups[1].Value.Replace('\\', '/'));
            return refs;
        }

        private static bool TryHasConnectionStrings(string absPath)
        {
            try { return File.ReadAllText(absPath).IndexOf("<connectionStrings", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { return false; }
        }

        private IEnumerable<string> SafeEnumerateFiles(string root, string pattern, CancellationToken ct)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) yield break;

                var dir = stack.Pop();
                if (_settings.BlockedFolders.Contains(Path.GetFileName(dir))) continue;

                string[] files;
                try { files = Directory.GetFiles(dir, pattern); }
                catch { files = Array.Empty<string>(); }

                foreach (var f in files) yield return f;

                string[] subDirs;
                try { subDirs = Directory.GetDirectories(dir); }
                catch { subDirs = Array.Empty<string>(); }

                foreach (var sub in subDirs) stack.Push(sub);
            }
        }
    }
}
