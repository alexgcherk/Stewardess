// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
namespace StewardessMCPService.CodeIndexing.Model.Structural;

/// <summary>
/// Represents a half-open source location span within a file.
/// Line and column numbers are 1-based.
/// </summary>
public sealed class SourceSpan
{
    /// <summary>1-based start line.</summary>
    public int StartLine { get; init; }

    /// <summary>1-based start column.</summary>
    public int StartColumn { get; init; }

    /// <summary>1-based end line (inclusive).</summary>
    public int EndLine { get; init; }

    /// <summary>1-based end column (inclusive).</summary>
    public int EndColumn { get; init; }

    /// <summary>Optional absolute byte offset of the start position.</summary>
    public int? StartOffset { get; init; }

    /// <summary>Optional absolute byte offset of the end position.</summary>
    public int? EndOffset { get; init; }

    /// <summary>Creates a <see cref="SourceSpan"/> covering the given line range.</summary>
    public static SourceSpan FromLines(int startLine, int endLine) =>
        new() { StartLine = startLine, StartColumn = 1, EndLine = endLine, EndColumn = int.MaxValue };

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{StartLine}:{StartColumn}-{EndLine}:{EndColumn}]";
}
