using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Full implementation of <see cref="IFileSystemService"/>.
    /// All disk paths flow through <see cref="PathValidator"/> before any I/O.
    /// </summary>
    public sealed class FileSystemService : IFileSystemService
    {
        private readonly McpServiceSettings _settings;
        private readonly PathValidator _pathValidator;
        private readonly IAuditService _audit;
        private static readonly McpLogger _log = McpLogger.For<FileSystemService>();

        /// <summary>Initialises a new instance of <see cref="FileSystemService"/>.</summary>
        public FileSystemService(McpServiceSettings settings, PathValidator pathValidator, IAuditService audit)
        {
            _settings      = settings      ?? throw new ArgumentNullException(nameof(settings));
            _pathValidator = pathValidator  ?? throw new ArgumentNullException(nameof(pathValidator));
            _audit         = audit          ?? throw new ArgumentNullException(nameof(audit));
        }

        // ── Repository info ──────────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<RepositoryInfoResponse> GetRepositoryInfoAsync(CancellationToken ct = default)
        {
            var root  = _settings.RepositoryRoot;
            var info  = new DirectoryInfo(root);

            GitRepoSummary git = null;
            var gitDir = Path.Combine(root, ".git");
            if (Directory.Exists(gitDir))
            {
                git = new GitRepoSummary { IsGitRepository = true };
                git.CurrentBranch  = TryReadGitHead(gitDir);
                git.HeadCommitSha  = TryReadHeadCommitSha(gitDir, git.CurrentBranch);
            }

            var policy = BuildPolicyInfo();

            var response = new RepositoryInfoResponse
            {
                RepositoryRoot   = root,
                RepositoryName   = info.Name,
                ReadOnlyMode     = _settings.ReadOnlyMode,
                ServiceVersion   = _settings.ServiceVersion,
                ServerTime       = DateTimeOffset.UtcNow,
                Policy           = policy,
                GitInfo          = git ?? new GitRepoSummary { IsGitRepository = false }
            };

            return Task.FromResult(response);
        }

        // ── Directory navigation ─────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<ListDirectoryResponse> ListDirectoryAsync(ListDirectoryRequest request, CancellationToken ct = default)
        {
            var validation = _pathValidator.Validate(request?.Path ?? "", out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!Directory.Exists(absPath))
                throw new DirectoryNotFoundException($"Directory not found: {request.Path}");

            var dirInfo   = new DirectoryInfo(absPath);
            var allItems  = dirInfo.GetFileSystemInfos();

            var entries = new List<DirectoryEntry>();
            foreach (var item in allItems)
            {
                bool isBlocked = _pathValidator.IsFolderBlocked(item.FullName)
                              || (_settings.BlockedFolders.Contains(item.Name));

                if (isBlocked && !request.IncludeBlocked) continue;

                if (!string.IsNullOrEmpty(request.NamePattern) &&
                    !MatchesWildcard(item.Name, request.NamePattern))
                    continue;

                var entry = new DirectoryEntry
                {
                    Name         = item.Name,
                    RelativePath = _pathValidator.ToRelativePath(item.FullName),
                    IsBlocked    = isBlocked,
                    LastModified = new DateTimeOffset(item.LastWriteTimeUtc, TimeSpan.Zero)
                };

                if (item is FileInfo fi)
                {
                    entry.Type      = "file";
                    entry.SizeBytes = fi.Length;
                    entry.Extension = fi.Extension.ToLowerInvariant();
                }
                else
                {
                    entry.Type = "directory";
                }

                entries.Add(entry);
            }

            var sorted   = entries.OrderBy(e => e.Type).ThenBy(e => e.Name).ToList();
            bool truncated = sorted.Count > _settings.MaxDirectoryEntries;
            if (truncated) sorted = sorted.Take(_settings.MaxDirectoryEntries).ToList();

            return Task.FromResult(new ListDirectoryResponse
            {
                Path         = _pathValidator.ToRelativePath(absPath),
                AbsolutePath = absPath,
                Entries      = sorted,
                TotalCount   = allItems.Length,
                Truncated    = truncated
            });
        }

        /// <inheritdoc />
        public Task<ListTreeResponse> ListTreeAsync(ListTreeRequest request, CancellationToken ct = default)
        {
            var validation = _pathValidator.Validate(request?.Path ?? "", out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!Directory.Exists(absPath))
                throw new DirectoryNotFoundException($"Directory not found: {request.Path}");

            // maxDepth < 0 means "use the configured limit" (callers pass -1 for unbounded).
            int maxDepth = request.MaxDepth < 0
                ? _settings.MaxDirectoryDepth
                : Math.Min(request.MaxDepth, _settings.MaxDirectoryDepth);
            int totalFiles = 0, totalDirs = 0;
            bool truncated = false;

            var root = BuildTreeNode(absPath, maxDepth, 0, request,
                                     ref totalFiles, ref totalDirs, ref truncated);

            return Task.FromResult(new ListTreeResponse
            {
                Path             = request.Path ?? "",
                Root             = root,
                TotalFiles       = totalFiles,
                TotalDirectories = totalDirs,
                Truncated        = truncated
            });
        }

        /// <inheritdoc />
        public Task<PathExistsResponse> PathExistsAsync(string relativePath, CancellationToken ct = default)
        {
            var validation = _pathValidator.Validate(relativePath, out var absPath);
            if (!validation.IsValid)
                return Task.FromResult(new PathExistsResponse { Path = relativePath, Exists = false, Type = "none" });

            string type = "none";
            if (File.Exists(absPath))      type = "file";
            else if (Directory.Exists(absPath)) type = "directory";

            return Task.FromResult(new PathExistsResponse
            {
                Path   = relativePath,
                Exists = type != "none",
                Type   = type
            });
        }

        /// <inheritdoc />
        public Task<FileMetadataResponse> GetMetadataAsync(FileMetadataRequest request, CancellationToken ct = default)
        {
            var validation = _pathValidator.ValidateRead(request?.Path, out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            var relPath = _pathValidator.ToRelativePath(absPath);

            if (File.Exists(absPath))
            {
                var fi = new FileInfo(absPath);
                var response = new FileMetadataResponse
                {
                    RelativePath  = relPath,
                    AbsolutePath  = absPath,
                    Name          = fi.Name,
                    Extension     = fi.Extension.ToLowerInvariant(),
                    Type          = "file",
                    SizeBytes     = fi.Length,
                    CreatedAt     = new DateTimeOffset(fi.CreationTimeUtc,  TimeSpan.Zero),
                    LastModified  = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
                    LastAccessed  = new DateTimeOffset(fi.LastAccessTimeUtc,TimeSpan.Zero),
                    IsReadOnly    = fi.IsReadOnly
                };

                if (fi.Length <= _settings.MaxFileReadBytes)
                {
                    response.Encoding    = DetectFileEncodingSync(absPath);
                    response.LineEnding  = DetectLineEndingSync(absPath);
                    response.LineCount   = CountLinesSync(absPath);
                }
                return Task.FromResult(response);
            }

            if (Directory.Exists(absPath))
            {
                var di = new DirectoryInfo(absPath);
                return Task.FromResult(new FileMetadataResponse
                {
                    RelativePath  = relPath,
                    AbsolutePath  = absPath,
                    Name          = di.Name,
                    Type          = "directory",
                    CreatedAt     = new DateTimeOffset(di.CreationTimeUtc,  TimeSpan.Zero),
                    LastModified  = new DateTimeOffset(di.LastWriteTimeUtc, TimeSpan.Zero),
                    LastAccessed  = new DateTimeOffset(di.LastAccessTimeUtc,TimeSpan.Zero)
                });
            }

            throw new FileNotFoundException($"Path not found: {request.Path}");
        }

        // ── File reading ─────────────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<ReadFileResponse> ReadFileAsync(ReadFileRequest request, CancellationToken ct = default)
        {
            var validation = _pathValidator.ValidateRead(request?.Path, out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {request.Path}");

            var fi       = new FileInfo(absPath);
            long maxBytes = request.MaxBytes.HasValue
                ? Math.Min(request.MaxBytes.Value, _settings.MaxFileReadBytes)
                : _settings.MaxFileReadBytes;

            bool isBinary   = IsBinaryFile(absPath);
            bool truncated  = fi.Length > maxBytes;
            long toRead     = truncated ? maxBytes : fi.Length;

            string content       = null;
            string contentBase64 = null;
            string encoding      = "utf-8";
            string lineEnding    = "lf";
            int lineCount        = 0;

            if (request.ReturnBase64 || isBinary)
            {
                var bytes = await ReadBytesAsync(absPath, toRead, ct).ConfigureAwait(false);
                contentBase64 = Convert.ToBase64String(bytes);
            }
            else
            {
                encoding  = DetectFileEncodingSync(absPath);
                var enc   = ResolveEncoding(encoding);
                content   = await ReadTextAsync(absPath, toRead, enc, ct).ConfigureAwait(false);
                lineEnding = DetectLineEndingInText(content);
                lineCount  = CountNewlines(content) + 1;
            }

            return new ReadFileResponse
            {
                RelativePath  = _pathValidator.ToRelativePath(absPath),
                Name          = fi.Name,
                Extension     = fi.Extension.ToLowerInvariant(),
                Content       = content,
                ContentBase64 = contentBase64,
                SizeBytes     = fi.Length,
                LineCount     = lineCount,
                Encoding      = encoding,
                LineEnding    = lineEnding,
                LastModified  = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
                IsBinary      = isBinary,
                Truncated     = truncated,
                BytesReturned = toRead
            };
        }

        /// <inheritdoc />
        public async Task<ReadFileRangeResponse> ReadFileRangeAsync(ReadFileRangeRequest request, CancellationToken ct = default)
        {
            var validation = _pathValidator.ValidateRead(request?.Path, out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {request.Path}");

            var allLines = await Task.Run(() => File.ReadAllLines(absPath), ct).ConfigureAwait(false);
            int total    = allLines.Length;
            int start    = Math.Max(1, request.StartLine);
            int end      = request.EndLine < 1 ? total : Math.Min(request.EndLine, total);

            if (start > total)
                throw new ArgumentOutOfRangeException(nameof(request.StartLine),
                    $"StartLine {start} exceeds file length ({total} lines).");

            var lines      = new List<FileLine>();
            var sbContent  = new StringBuilder();

            for (int i = start; i <= end; i++)
            {
                var lineText = allLines[i - 1];
                lines.Add(new FileLine { LineNumber = i, Text = lineText });

                if (request.IncludeLineNumbers)
                    sbContent.AppendFormat("{0,6}: {1}{2}", i, lineText, Environment.NewLine);
                else
                    sbContent.AppendLine(lineText);
            }

            return new ReadFileRangeResponse
            {
                RelativePath = _pathValidator.ToRelativePath(absPath),
                StartLine    = start,
                EndLine      = end,
                TotalLines   = total,
                Content      = sbContent.ToString(),
                Lines        = lines
            };
        }

        /// <inheritdoc />
        public async Task<ReadMultipleFilesResponse> ReadMultipleFilesAsync(ReadMultipleFilesRequest request, CancellationToken ct = default)
        {
            var results = new List<FileReadResult>();

            foreach (var path in request.Paths ?? Enumerable.Empty<string>())
            {
                var result = new FileReadResult { RelativePath = path };
                try
                {
                    var readReq = new ReadFileRequest
                    {
                        Path     = path,
                        MaxBytes = request.MaxBytesPerFile
                    };
                    var readResult = await ReadFileAsync(readReq, ct).ConfigureAwait(false);
                    result.Success    = true;
                    result.Content    = readResult.Content;
                    result.SizeBytes  = readResult.SizeBytes;
                    result.Encoding   = readResult.Encoding;
                    result.Truncated  = readResult.Truncated;
                }
                catch (Exception ex)
                {
                    result.Success      = false;
                    result.ErrorMessage = ex.Message;
                }
                results.Add(result);
            }

            return new ReadMultipleFilesResponse { Files = results };
        }

        /// <inheritdoc />
        public async Task<FileHashResponse> GetFileHashAsync(FileHashRequest request, CancellationToken ct = default)
        {
            var validation = _pathValidator.ValidateRead(request?.Path, out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {request.Path}");

            var fi        = new FileInfo(absPath);
            var algorithm = (request.Algorithm ?? "SHA256").ToUpperInvariant();

            string hash;
            using (var stream = new FileStream(absPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                               bufferSize: 81920, useAsync: true))
            {
                byte[] hashBytes;
                switch (algorithm)
                {
                    case "MD5":    using (var md5  = MD5.Create())    hashBytes = await Task.Run(() => md5.ComputeHash(stream), ct);  break;
                    case "SHA1":   using (var sha1 = SHA1.Create())   hashBytes = await Task.Run(() => sha1.ComputeHash(stream), ct); break;
                    default:
                        algorithm = "SHA256";
                        using (var sha  = SHA256.Create()) hashBytes = await Task.Run(() => sha.ComputeHash(stream), ct);
                        break;
                }
                hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }

            return new FileHashResponse
            {
                RelativePath = _pathValidator.ToRelativePath(absPath),
                Algorithm    = algorithm,
                Hash         = hash,
                SizeBytes    = fi.Length
            };
        }

        /// <inheritdoc />
        public Task<FileStructureSummaryResponse> GetFileStructureSummaryAsync(
            FileStructureSummaryRequest request, CancellationToken ct = default)
        {
            var validation = _pathValidator.ValidateRead(request?.Path, out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            if (!File.Exists(absPath))
                throw new FileNotFoundException($"File not found: {request.Path}");

            var ext      = Path.GetExtension(absPath).ToLowerInvariant();
            var language = MapExtensionToLanguage(ext);

            var lines    = File.ReadAllLines(absPath);
            var summary  = ParseCodeStructure(lines, language,
                               _pathValidator.ToRelativePath(absPath));

            return Task.FromResult(summary);
        }

        // ── Encoding / format helpers ────────────────────────────────────────────

        /// <inheritdoc />
        public Task<string> DetectEncodingAsync(string relativePath, CancellationToken ct = default)
        {
            var validation = _pathValidator.ValidateRead(relativePath, out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            return Task.FromResult(DetectFileEncodingSync(absPath));
        }

        /// <inheritdoc />
        public Task<string> DetectLineEndingAsync(string relativePath, CancellationToken ct = default)
        {
            var validation = _pathValidator.ValidateRead(relativePath, out var absPath);
            if (!validation.IsValid)
                throw new ArgumentException(validation.ErrorMessage);

            return Task.FromResult(DetectLineEndingSync(absPath));
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private TreeNode BuildTreeNode(string absPath, int maxDepth, int currentDepth,
            ListTreeRequest request, ref int totalFiles, ref int totalDirs, ref bool truncated)
        {
            var info  = new DirectoryInfo(absPath);
            bool blocked = _settings.BlockedFolders.Contains(info.Name);

            var node = new TreeNode
            {
                Name         = info.Name,
                RelativePath = _pathValidator.ToRelativePath(absPath),
                Type         = "directory",
                LastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                IsBlocked    = blocked
            };

            if (blocked && !request.IncludeBlocked) return node;
            if (blocked) return node; // show but don't expand

            if (currentDepth >= maxDepth) return node;

            var children = new List<TreeNode>();
            FileSystemInfo[] items;
            try { items = info.GetFileSystemInfos(); }
            catch { return node; }

            foreach (var item in items)
            {
                if (item is DirectoryInfo di)
                {
                    bool childBlocked = _settings.BlockedFolders.Contains(di.Name);
                    if (childBlocked && !request.IncludeBlocked) continue;

                    totalDirs++;
                    var child = BuildTreeNode(di.FullName, maxDepth, currentDepth + 1,
                                              request, ref totalFiles, ref totalDirs, ref truncated);
                    children.Add(child);
                }
                else if (!request.DirectoriesOnly && item is FileInfo fi)
                {
                    if (request.ExtensionFilter?.Count > 0 &&
                        !request.ExtensionFilter.Contains(fi.Extension, StringComparer.OrdinalIgnoreCase))
                        continue;

                    totalFiles++;
                    children.Add(new TreeNode
                    {
                        Name         = fi.Name,
                        RelativePath = _pathValidator.ToRelativePath(fi.FullName),
                        Type         = "file",
                        SizeBytes    = fi.Length,
                        Extension    = fi.Extension.ToLowerInvariant(),
                        LastModified = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero)
                    });
                }
            }

            node.Children = children.OrderBy(c => c.Type).ThenBy(c => c.Name).ToList();
            return node;
        }

        private RepositoryPolicyInfo BuildPolicyInfo() => new RepositoryPolicyInfo
        {
            ApiKeyRequired                   = _settings.RequireApiKey,
            IpAllowlistActive                = _settings.AllowedIPs.Count > 0,
            ReadOnlyMode                     = _settings.ReadOnlyMode,
            ApprovalRequiredForDestructive   = _settings.RequireApprovalForDestructive,
            BlockedFolders                   = _settings.BlockedFolders.ToList(),
            BlockedExtensions                = _settings.BlockedExtensions.ToList(),
            AllowedExtensions                = _settings.AllowedExtensions.ToList(),
            MaxFileReadBytes                 = _settings.MaxFileReadBytes,
            MaxSearchResults                 = _settings.MaxSearchResults,
            MaxDirectoryDepth                = _settings.MaxDirectoryDepth
        };

        // ── Git head helpers ─────────────────────────────────────────────────────

        private static string TryReadGitHead(string gitDir)
        {
            try
            {
                var headFile = Path.Combine(gitDir, "HEAD");
                if (!File.Exists(headFile)) return null;
                var content = File.ReadAllText(headFile).Trim();
                if (content.StartsWith("ref: refs/heads/"))
                    return content.Substring("ref: refs/heads/".Length);
                return content.Length >= 7 ? content.Substring(0, 7) : content;
            }
            catch { return null; }
        }

        private static string TryReadHeadCommitSha(string gitDir, string branch)
        {
            try
            {
                if (string.IsNullOrEmpty(branch)) return null;
                var refFile = Path.Combine(gitDir, "refs", "heads", branch);
                if (File.Exists(refFile))
                    return File.ReadAllText(refFile).Trim();
                // packed-refs fallback
                var packed = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packed))
                {
                    foreach (var line in File.ReadAllLines(packed))
                    {
                        if (line.EndsWith("refs/heads/" + branch, StringComparison.OrdinalIgnoreCase))
                            return line.Split(' ')[0];
                    }
                }
                return null;
            }
            catch { return null; }
        }

        // ── Encoding detection ───────────────────────────────────────────────────

        internal static string DetectFileEncodingSync(string absPath)
        {
            try
            {
                using (var stream = new FileStream(absPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var bom = new byte[4];
                    int read = stream.Read(bom, 0, 4);
                    if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return "utf-8-bom";
                    if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return "utf-16-le";
                    if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return "utf-16-be";
                    if (read >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xFE && bom[3] == 0xFF) return "utf-32";
                    return "utf-8";
                }
            }
            catch { return "utf-8"; }
        }

        internal static string DetectLineEndingSync(string absPath)
        {
            try
            {
                var sample = new char[4096];
                using (var reader = new StreamReader(absPath))
                {
                    reader.Read(sample, 0, sample.Length);
                }
                return DetectLineEndingInText(new string(sample));
            }
            catch { return "lf"; }
        }

        private static string DetectLineEndingInText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "lf";
            bool hasCrlf = text.Contains("\r\n");
            bool hasCr   = text.Replace("\r\n", "").Contains('\r');
            bool hasLf   = text.Replace("\r\n", "").Contains('\n');

            if (hasCrlf && !hasCr && !hasLf) return "crlf";
            if (!hasCrlf && hasCr && !hasLf) return "cr";
            if (!hasCrlf && !hasCr && hasLf) return "lf";
            if (hasCrlf || hasCr || hasLf)   return "mixed";
            return "lf";
        }

        private static int CountLinesSync(string absPath)
        {
            try
            {
                int count = 0;
                using (var sr = new StreamReader(absPath))
                    while (sr.ReadLine() != null) count++;
                return count;
            }
            catch { return 0; }
        }

        private static int CountNewlines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            foreach (char c in text)
                if (c == '\n') count++;
            return count;
        }

        // ── Binary detection ─────────────────────────────────────────────────────

        private static readonly HashSet<string> _knownBinaryExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ".png",".jpg",".jpeg",".gif",".bmp",".ico",".tiff",".webp",
            ".mp3",".mp4",".avi",".mkv",".mov",".wav",".ogg",
            ".zip",".7z",".rar",".tar",".gz",".bz2",
            ".exe",".dll",".pdb",".so",".bin",".dat",
            ".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx"
        };

        internal static bool IsBinaryFile(string absPath)
        {
            var ext = Path.GetExtension(absPath);
            if (_knownBinaryExtensions.Contains(ext)) return true;

            try
            {
                // Read first 8 KB and look for null bytes.
                var buf = new byte[8192];
                using (var stream = new FileStream(absPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int read = stream.Read(buf, 0, buf.Length);
                    for (int i = 0; i < read; i++)
                        if (buf[i] == 0) return true;
                }
            }
            catch { }
            return false;
        }

        // ── I/O helpers ──────────────────────────────────────────────────────────

        private static async Task<byte[]> ReadBytesAsync(string absPath, long maxBytes, CancellationToken ct)
        {
            var buf = new byte[(int)Math.Min(maxBytes, int.MaxValue)];
            using (var stream = new FileStream(absPath, FileMode.Open, FileAccess.Read,
                                               FileShare.Read, 81920, useAsync: true))
            {
                int read = await stream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                if (read < buf.Length)
                    Array.Resize(ref buf, read);
            }
            return buf;
        }

        private static async Task<string> ReadTextAsync(string absPath, long maxBytes, Encoding enc, CancellationToken ct)
        {
            var bytes = await ReadBytesAsync(absPath, maxBytes, ct).ConfigureAwait(false);
            return enc.GetString(bytes);
        }

        private static Encoding ResolveEncoding(string name)
        {
            switch (name?.ToLowerInvariant())
            {
                case "utf-8-bom":  return new UTF8Encoding(true);
                case "utf-16-le":  return Encoding.Unicode;
                case "utf-16-be":  return Encoding.BigEndianUnicode;
                case "utf-32":     return Encoding.UTF32;
                case "ascii":      return Encoding.ASCII;
                default:           return new UTF8Encoding(false);
            }
        }

        // ── Wildcard matching ────────────────────────────────────────────────────

        private static bool MatchesWildcard(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase);
        }

        // ── Code structure parsing ───────────────────────────────────────────────

        private static readonly Regex _nsRegex     = new Regex(@"^\s*namespace\s+([\w.]+)",  RegexOptions.Compiled);
        private static readonly Regex _typeRegex   = new Regex(@"^\s*(public|internal|protected|private|sealed|abstract|static|partial).*?(class|interface|struct|enum)\s+(\w+)", RegexOptions.Compiled);
        private static readonly Regex _methodRegex = new Regex(@"^\s*(public|internal|protected|private|static|virtual|override|async|sealed|abstract)\s+[\w<>\[\],\s]+\s+(\w+)\s*\(", RegexOptions.Compiled);
        private static readonly Regex _usingRegex  = new Regex(@"^\s*using\s+([\w.]+)\s*;",  RegexOptions.Compiled);

        private static FileStructureSummaryResponse ParseCodeStructure(string[] lines, string language, string relPath)
        {
            var result = new FileStructureSummaryResponse
            {
                RelativePath = relPath,
                Language     = language
            };

            NamespaceInfo currentNs = null;
            TypeInfo      currentType = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var usingMatch = _usingRegex.Match(line);
                if (usingMatch.Success)
                {
                    result.UsingDirectives.Add(usingMatch.Groups[1].Value);
                    continue;
                }

                var nsMatch = _nsRegex.Match(line);
                if (nsMatch.Success)
                {
                    currentNs = new NamespaceInfo { Name = nsMatch.Groups[1].Value, LineNumber = i + 1 };
                    result.Namespaces.Add(currentNs);
                    currentType = null;
                    continue;
                }

                var typeMatch = _typeRegex.Match(line);
                if (typeMatch.Success)
                {
                    currentType = new TypeInfo
                    {
                        Kind       = typeMatch.Groups[2].Value,
                        Name       = typeMatch.Groups[3].Value,
                        Namespace  = currentNs?.Name,
                        LineNumber = i + 1
                    };

                    if (currentNs != null) currentNs.Types.Add(currentType);
                    else result.TopLevelTypes.Add(currentType);
                    continue;
                }

                if (currentType != null)
                {
                    var memberMatch = _methodRegex.Match(line);
                    if (memberMatch.Success)
                    {
                        currentType.Members.Add(new MemberInfo
                        {
                            Kind       = "method",
                            Name       = memberMatch.Groups[2].Value,
                            LineNumber = i + 1,
                            IsPublic   = line.Contains("public"),
                            IsStatic   = line.Contains("static")
                        });
                    }
                }
            }

            return result;
        }

        private static string MapExtensionToLanguage(string ext)
        {
            switch (ext)
            {
                case ".cs":    return "csharp";
                case ".vb":    return "vbnet";
                case ".fs":    return "fsharp";
                case ".ts":    return "typescript";
                case ".js":    return "javascript";
                case ".py":    return "python";
                case ".java":  return "java";
                case ".go":    return "go";
                case ".rs":    return "rust";
                case ".cpp": case ".cc": case ".cxx": return "cpp";
                case ".c":     return "c";
                case ".json":  return "json";
                case ".xml":   return "xml";
                case ".html":  return "html";
                case ".css":   return "css";
                case ".sql":   return "sql";
                case ".sh":    return "shell";
                case ".ps1":   return "powershell";
                case ".md":    return "markdown";
                default:       return "text";
            }
        }
    }
}
