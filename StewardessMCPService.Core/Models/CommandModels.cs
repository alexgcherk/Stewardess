// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.Models;
// ────────────────────────────────────────────────────────────────────────────
//  Command / build / test execution models
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Request to run the repository's build command.</summary>
public sealed class RunBuildRequest
{
    /// <summary>
    ///     Working directory relative to repository root.  Empty = root.
    ///     Typically the directory containing a .sln or .csproj file.
    /// </summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>
    ///     Build tool to use: "dotnet build", "msbuild", or another allowed command.
    ///     Defaults to "dotnet build".
    /// </summary>
    public string BuildCommand { get; set; } = "dotnet build";

    /// <summary>Additional CLI arguments appended to the build command.</summary>
    public string Arguments { get; set; } = "";

    /// <summary>Target configuration: "Debug" (default) or "Release".</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>Override the server's timeout (seconds); capped at MaxCommandExecutionSeconds.</summary>
    public int? TimeoutSeconds { get; set; }
}

/// <summary>Request to run the repository's test suite.</summary>
public sealed class RunTestsRequest
{
    /// <summary>Working directory relative to repository root. Empty = root.</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>Test command: "dotnet test" (default) or another allowed command.</summary>
    public string TestCommand { get; set; } = "dotnet test";

    /// <summary>Extra arguments appended to the test command.</summary>
    public string Arguments { get; set; } = "";

    /// <summary>Filter expression passed to the test runner (e.g. dotnet test --filter).</summary>
    public string Filter { get; set; } = "";

    /// <summary>Build configuration to use, e.g. "Debug" or "Release".</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>Optional override for the maximum allowed execution time in seconds.</summary>
    public int? TimeoutSeconds { get; set; }
}

/// <summary>
///     Request to execute a custom command from the server's AllowedCommands whitelist.
/// </summary>
public sealed class RunCustomCommandRequest
{
    /// <summary>
    ///     The full command line to execute.  Must start with an entry in AllowedCommands.
    /// </summary>
    public string Command { get; set; } = null!;

    /// <summary>Working directory relative to repository root.  Empty = root.</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>Optional override for the maximum allowed execution time in seconds.</summary>
    public int? TimeoutSeconds { get; set; }
}

// ────────────────────────────────────────────────────────────────────────────
//  Command result
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Result of executing a build, test, or custom command.</summary>
public sealed class CommandResult
{
    /// <summary>The full command that was executed.</summary>
    public string Command { get; set; } = null!;

    /// <summary>Working directory used.</summary>
    public string WorkingDirectory { get; set; } = null!;

    /// <summary>Process exit code.</summary>
    public int ExitCode { get; set; }

    /// <summary>True when ExitCode is 0.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Standard output (may be truncated for large outputs).</summary>
    public string? StandardOutput { get; set; }

    /// <summary>Standard error output.</summary>
    public string? StandardError { get; set; }

    /// <summary>Combined stdout + stderr in execution order (where available).</summary>
    public string? CombinedOutput { get; set; }

    /// <summary>Wall-clock execution time in milliseconds.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>True when the process was killed due to a timeout.</summary>
    public bool TimedOut { get; set; }

    /// <summary>Timeout that was applied in seconds.</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>True when output was truncated at the server limit.</summary>
    public bool OutputTruncated { get; set; }

    /// <summary>UTC timestamp when execution started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Parsed build/test summary if applicable.</summary>
    public BuildSummary? Summary { get; set; }
}

/// <summary>High-level build summary extracted from command output.</summary>
public sealed class BuildSummary
{
    /// <summary>Total number of build errors.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Total number of build warnings.</summary>
    public int WarningCount { get; set; }

    /// <summary>Parsed build error diagnostics.</summary>
    public List<BuildDiagnostic> Errors { get; set; } = new();

    /// <summary>Parsed build warning diagnostics.</summary>
    public List<BuildDiagnostic> Warnings { get; set; } = new();

    // test-specific
    /// <summary>Number of tests that passed (null if not a test run).</summary>
    public int? TestsPassed { get; set; }

    /// <summary>Number of tests that failed (null if not a test run).</summary>
    public int? TestsFailed { get; set; }

    /// <summary>Number of tests that were skipped (null if not a test run).</summary>
    public int? TestsSkipped { get; set; }

    /// <summary>Total number of tests discovered (null if not a test run).</summary>
    public int? TestsTotal { get; set; }
}

/// <summary>A single compiler error or warning parsed from build output.</summary>
public sealed class BuildDiagnostic
{
    /// <summary>"error" or "warning".</summary>
    public string Severity { get; set; } = null!;

    /// <summary>Compiler diagnostic code (e.g. CS0001).</summary>
    public string Code { get; set; } = null!;

    /// <summary>Human-readable diagnostic message.</summary>
    public string Message { get; set; } = null!;

    /// <summary>Source file path where the diagnostic was raised.</summary>
    public string? FilePath { get; set; }

    /// <summary>1-based line number of the diagnostic.</summary>
    public int? Line { get; set; }

    /// <summary>1-based column number of the diagnostic.</summary>
    public int? Column { get; set; }
}