using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Services
{
    /// <summary>
    /// Executes build, test, and custom shell commands that appear on the configured
    /// AllowedCommands whitelist.  No unrestricted shell access is provided.
    /// </summary>
    public sealed class CommandService : ICommandService
    {
        private readonly McpServiceSettings _settings;
        private readonly PathValidator      _pathValidator;
        private readonly IAuditService      _auditService;
        private static readonly McpLogger   _log = McpLogger.For<CommandService>();

        // Maximum output captured from a single command (512 KB).
        private const int MaxOutputBytes = 512 * 1024;

        /// <summary>Initialises a new instance of <see cref="CommandService"/>.</summary>
        public CommandService(
            McpServiceSettings settings,
            PathValidator      pathValidator,
            IAuditService      auditService)
        {
            _settings      = settings      ?? throw new ArgumentNullException(nameof(settings));
            _pathValidator = pathValidator  ?? throw new ArgumentNullException(nameof(pathValidator));
            _auditService  = auditService   ?? throw new ArgumentNullException(nameof(auditService));
        }

        // ── ICommandService ──────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool IsCommandAllowed(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            var trimmed = command.Trim();
            return _settings.AllowedCommands.Any(allowed =>
                trimmed.StartsWith(allowed, StringComparison.OrdinalIgnoreCase));
        }

        /// <inheritdoc/>
        public Task<CommandResult> RunBuildAsync(RunBuildRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            EnsureWriteAllowed();

            var buildCmd = string.IsNullOrWhiteSpace(request.BuildCommand)
                ? "dotnet build"
                : request.BuildCommand.Trim();

            var args = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(request.Arguments))
                args.Append(" " + request.Arguments.Trim());
            if (!string.IsNullOrWhiteSpace(request.Configuration))
                args.Append($" --configuration {request.Configuration}");

            var fullCommand = buildCmd + args;

            if (!IsCommandAllowed(fullCommand))
                throw new UnauthorizedAccessException(
                    $"Build command not in AllowedCommands list: {buildCmd}");

            return RunInternalAsync(
                fullCommand,
                request.WorkingDirectory,
                request.TimeoutSeconds,
                isBuild: true,
                ct: ct);
        }

        /// <inheritdoc/>
        public Task<CommandResult> RunTestsAsync(RunTestsRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            EnsureWriteAllowed();

            var testCmd = string.IsNullOrWhiteSpace(request.TestCommand)
                ? "dotnet test"
                : request.TestCommand.Trim();

            var args = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(request.Arguments))
                args.Append(" " + request.Arguments.Trim());
            if (!string.IsNullOrWhiteSpace(request.Configuration))
                args.Append($" --configuration {request.Configuration}");
            if (!string.IsNullOrWhiteSpace(request.Filter))
                args.Append($" --filter \"{request.Filter}\"");

            var fullCommand = testCmd + args;

            if (!IsCommandAllowed(fullCommand))
                throw new UnauthorizedAccessException(
                    $"Test command not in AllowedCommands list: {testCmd}");

            return RunInternalAsync(
                fullCommand,
                request.WorkingDirectory,
                request.TimeoutSeconds,
                isTest: true,
                ct: ct);
        }

        /// <inheritdoc/>
        public Task<CommandResult> RunCustomCommandAsync(
            RunCustomCommandRequest request, CancellationToken ct = default)
        {
            if (request == null)                              throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Command))  throw new ArgumentException("Command is required.", nameof(request));

            EnsureWriteAllowed();

            if (!IsCommandAllowed(request.Command))
                throw new UnauthorizedAccessException(
                    $"Command not in AllowedCommands list: {request.Command}");

            return RunInternalAsync(
                request.Command,
                request.WorkingDirectory,
                request.TimeoutSeconds,
                ct: ct);
        }

        // ── Core execution ───────────────────────────────────────────────────────

        private async Task<CommandResult> RunInternalAsync(
            string  fullCommand,
            string  relativeWorkDir,
            int?    timeoutOverride,
            bool    isBuild = false,
            bool    isTest  = false,
            CancellationToken ct = default)
        {
            var workDir   = ResolveWorkingDirectory(relativeWorkDir);
            var timeoutSec = ClampTimeout(timeoutOverride);
            var (filename, arguments) = SplitCommand(fullCommand);
            var startedAt = DateTimeOffset.UtcNow;
            var sw        = Stopwatch.StartNew();

            var stdoutSb  = new StringBuilder();
            var stderrSb  = new StringBuilder();
            var combinedSb= new StringBuilder();
            bool outputTruncated = false;

            var psi = new ProcessStartInfo
            {
                FileName               = filename,
                Arguments              = arguments,
                WorkingDirectory       = workDir,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            };

            var exitCode = -1;
            var timedOut = false;

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

                using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    var tcs = new TaskCompletionSource<int>();

                    process.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data == null) return;
                        if (combinedSb.Length < MaxOutputBytes)
                        {
                            stdoutSb.AppendLine(e.Data);
                            combinedSb.AppendLine(e.Data);
                        }
                        else outputTruncated = true;
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data == null) return;
                        if (combinedSb.Length < MaxOutputBytes)
                        {
                            stderrSb.AppendLine(e.Data);
                            combinedSb.AppendLine(e.Data);
                        }
                        else outputTruncated = true;
                    };
                    process.Exited += (_, __) => tcs.TrySetResult(process.ExitCode);

                    try
                    {
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        using (cts.Token.Register(() =>
                        {
                            timedOut = !ct.IsCancellationRequested;
                            try { process.Kill(); } catch { }
                            tcs.TrySetResult(-1);
                        }))
                        {
                            exitCode = await tcs.Task.ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { timedOut = !ct.IsCancellationRequested; throw; }
                    catch (Exception ex)
                    {
                        _log.Error($"Failed to start process '{filename}'", ex);
                        return new CommandResult
                        {
                            Command = fullCommand, WorkingDirectory = workDir,
                            ExitCode = -1, StandardError = ex.Message,
                            CombinedOutput = ex.Message, ElapsedMs = sw.ElapsedMilliseconds,
                            StartedAt = startedAt
                        };
                    }
                }
            }

            sw.Stop();
            var stdout  = stdoutSb.ToString();
            var stderr  = stderrSb.ToString();
            var combined = combinedSb.ToString();

            _log.LogCommand(fullCommand, exitCode, sw.ElapsedMilliseconds, timedOut);

            _ = _auditService.LogOperationAsync(
                requestId    : Guid.NewGuid().ToString("N"),
                sessionId    : null,
                operationType: isBuild ? AuditOperationType.RunBuild
                              : isTest  ? AuditOperationType.RunTests
                              :           AuditOperationType.RunCommand,
                operationName: isBuild ? "run_build" : isTest ? "run_tests" : "run_command",
                clientIp     : "internal",
                targetPath   : workDir,
                outcome      : exitCode == 0 ? AuditOutcome.Success : AuditOutcome.Failure,
                errorCode    : exitCode != 0 ? "EXIT_" + exitCode : null,
                description  : fullCommand,
                elapsedMs    : sw.ElapsedMilliseconds);

            BuildSummary summary = null;
            if (isBuild)  summary = ParseBuildSummary(stdout + stderr);
            if (isTest)   summary = ParseTestSummary(stdout + stderr);

            return new CommandResult
            {
                Command          = fullCommand,
                WorkingDirectory = workDir,
                ExitCode         = exitCode,
                Succeeded        = exitCode == 0,
                StandardOutput   = stdout,
                StandardError    = stderr,
                CombinedOutput   = combined,
                ElapsedMs        = sw.ElapsedMilliseconds,
                TimedOut         = timedOut,
                TimeoutSeconds   = timeoutSec,
                OutputTruncated  = outputTruncated,
                StartedAt        = startedAt,
                Summary          = summary
            };
        }

        // ── Build / test output parsing ──────────────────────────────────────────

        /// <summary>
        /// Extracts error and warning counts + locations from dotnet/MSBuild output.
        /// </summary>
        internal static BuildSummary ParseBuildSummary(string output)
        {
            var summary = new BuildSummary();
            if (string.IsNullOrEmpty(output)) return summary;

            // Match: src\File.cs(12,4): error CS0001: message
            var diagPattern = new Regex(
                @"^(?<file>[^(]+)\((?<line>\d+),(?<col>\d+)\): (?<sev>error|warning) (?<code>\w+): (?<msg>.+)$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (Match m in diagPattern.Matches(output))
            {
                var diag = new BuildDiagnostic
                {
                    FilePath = m.Groups["file"].Value.Trim(),
                    Line     = int.Parse(m.Groups["line"].Value),
                    Column   = int.Parse(m.Groups["col"].Value),
                    Code     = m.Groups["code"].Value,
                    Message  = m.Groups["msg"].Value.Trim(),
                    Severity = m.Groups["sev"].Value.ToLowerInvariant()
                };

                if (diag.Severity == "error")
                {
                    summary.Errors.Add(diag);
                    summary.ErrorCount++;
                }
                else
                {
                    summary.Warnings.Add(diag);
                    summary.WarningCount++;
                }
            }

            return summary;
        }

        /// <summary>
        /// Extracts test pass/fail/skip counts from dotnet test output.
        /// </summary>
        internal static BuildSummary ParseTestSummary(string output)
        {
            var summary = new BuildSummary();
            if (string.IsNullOrEmpty(output)) return summary;

            // "Total tests: 102   Passed: 100   Failed: 1   Skipped: 1"
            var totalMatch  = Regex.Match(output, @"Total tests:\s*(\d+)",  RegexOptions.IgnoreCase);
            var passMatch   = Regex.Match(output, @"Passed:\s*(\d+)",       RegexOptions.IgnoreCase);
            var failMatch   = Regex.Match(output, @"Failed:\s*(\d+)",       RegexOptions.IgnoreCase);
            var skipMatch   = Regex.Match(output, @"Skipped:\s*(\d+)",      RegexOptions.IgnoreCase);

            if (totalMatch.Success)  summary.TestsTotal   = int.Parse(totalMatch.Groups[1].Value);
            if (passMatch.Success)   summary.TestsPassed  = int.Parse(passMatch.Groups[1].Value);
            if (failMatch.Success)
            {
                summary.TestsFailed  = int.Parse(failMatch.Groups[1].Value);
                if (summary.TestsFailed > 0)
                {
                    summary.ErrorCount = summary.TestsFailed.Value;
                    summary.Errors.Add(new BuildDiagnostic
                    {
                        Severity = "error",
                        Code     = "TEST_FAILURE",
                        Message  = $"{summary.TestsFailed} test(s) failed."
                    });
                }
            }
            if (skipMatch.Success)   summary.TestsSkipped = int.Parse(skipMatch.Groups[1].Value);

            return summary;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void EnsureWriteAllowed()
        {
            if (_settings.ReadOnlyMode)
                throw new InvalidOperationException("Service is in read-only mode; command execution is disabled.");
        }

        private string ResolveWorkingDirectory(string relativeWorkDir)
        {
            if (string.IsNullOrWhiteSpace(relativeWorkDir))
                return _settings.RepositoryRoot;

            if (!_pathValidator.Validate(relativeWorkDir, out var absPath))
                throw new UnauthorizedAccessException($"Working directory is outside the repository root: {relativeWorkDir}");

            if (!Directory.Exists(absPath))
                throw new DirectoryNotFoundException($"Working directory not found: {relativeWorkDir}");

            return absPath;
        }

        private int ClampTimeout(int? requested)
        {
            var max = _settings.MaxCommandExecutionSeconds > 0
                ? _settings.MaxCommandExecutionSeconds
                : 60;
            if (requested == null || requested <= 0) return max;
            return Math.Min(requested.Value, max);
        }

        /// <summary>
        /// Splits "dotnet build --flag" into ("dotnet", "build --flag").
        /// </summary>
        internal static (string Filename, string Arguments) SplitCommand(string fullCommand)
        {
            if (string.IsNullOrWhiteSpace(fullCommand))
                return ("", "");

            var trimmed = fullCommand.Trim();
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0)
                return (trimmed, "");

            return (trimmed.Substring(0, spaceIdx), trimmed.Substring(spaceIdx + 1));
        }
    }
}
