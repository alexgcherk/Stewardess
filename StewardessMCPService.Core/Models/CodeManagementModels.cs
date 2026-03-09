// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.Models;

// ── /repository/info ─────────────────────────────────────────────────────────
public sealed class CmRepositoryInfoResponse
{
    public string Name { get; set; } = null!;
    public string RootPath { get; set; } = null!;
    public string DefaultBranch { get; set; } = null!;
    public List<CmLanguageInfo> Languages { get; set; } = new();
    public List<string> IgnoreRules { get; set; } = new();
    public CmPolicy Policy { get; set; } = null!;
}

public sealed class CmLanguageInfo
{
    public string Name { get; set; } = null!;
    public int FileCount { get; set; }
    public double Percent { get; set; }
}

public sealed class CmPolicy
{
    public bool AllowsEdits { get; set; }
    public bool RequiresApprovalForDelete { get; set; }
    public bool RequiresApprovalForRename { get; set; }
    public long MaxReadBytes { get; set; }
    public long MaxWriteBytes { get; set; }
}

// ── /repository/tree ─────────────────────────────────────────────────────────
public sealed class CmRepositoryTreeRequest
{
    public string Path { get; set; } = ".";
    public int Depth { get; set; } = 4;
    public List<string>? IncludeGlobs { get; set; }
    public List<string>? ExcludeGlobs { get; set; }
    public bool RespectIgnore { get; set; } = true;
    public bool IncludeFiles { get; set; } = true;
    public bool IncludeDirectories { get; set; } = true;
    public int MaxEntries { get; set; } = 5000;
}

public sealed class CmRepositoryTreeResponse
{
    public string Root { get; set; } = null!;
    public List<CmTreeEntry> Entries { get; set; } = new();
}

public sealed class CmTreeEntry
{
    public string Path { get; set; } = null!;
    public string Kind { get; set; } = null!; /* "file" | "directory" */
    public long? Size { get; set; }
}

// ── /search/files ────────────────────────────────────────────────────────────
public sealed class CmFileSearchRequest
{
    public string Query { get; set; } = null!;
    public string MatchMode { get; set; } = "pathContains";
    public string BasePath { get; set; } = ".";
    public List<string>? Extensions { get; set; }
    public List<string>? Languages { get; set; }
    public List<string>? ExcludeGlobs { get; set; }
    public bool RespectIgnore { get; set; } = true;
    public int MaxResults { get; set; } = 100;
    public bool IncludeMetadata { get; set; } = true;
}

public sealed class CmFileSearchResponse
{
    public List<CmFileSearchResult> Results { get; set; } = new();
}

public sealed class CmFileSearchResult
{
    public string Path { get; set; } = null!;
    public string? Language { get; set; }
    public long? Size { get; set; }
    public double? Score { get; set; }
}

// ── /search/text ─────────────────────────────────────────────────────────────
public sealed class CmTextSearchRequest
{
    public string Pattern { get; set; } = null!;
    public bool IsRegex { get; set; } = false;
    public bool CaseSensitive { get; set; } = false;
    public bool WholeWord { get; set; } = false;
    public string BasePath { get; set; } = ".";
    public List<string>? IncludeGlobs { get; set; }
    public List<string>? ExcludeGlobs { get; set; }
    public List<string>? Languages { get; set; }
    public int ContextBefore { get; set; } = 2;
    public int ContextAfter { get; set; } = 2;
    public bool RespectIgnore { get; set; } = true;
    public int MaxResults { get; set; } = 200;
}

public sealed class CmTextSearchResponse
{
    public List<CmTextMatch> Matches { get; set; } = new();
}

public sealed class CmTextMatch
{
    public string Path { get; set; } = null!;
    public int Line { get; set; }
    public int Column { get; set; }
    public string Preview { get; set; } = null!;
    public List<string> ContextBefore { get; set; } = new();
    public List<string> ContextAfter { get; set; } = new();
}

// ── /files/read ──────────────────────────────────────────────────────────────
public sealed class CmLineRange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public sealed class CmReadFileRequest
{
    public string Path { get; set; } = null!;
    public List<CmLineRange>? Ranges { get; set; }
    public bool IncludeLineNumbers { get; set; } = true;
    public bool NormalizeNewlines { get; set; } = true;
    public long MaxBytes { get; set; } = 250000;
}

public sealed class CmReadFileResponse
{
    public string Path { get; set; } = null!;
    public string? Etag { get; set; }
    public string Encoding { get; set; } = null!;
    public string Newline { get; set; } = null!;
    public string Content { get; set; } = null!;
    public int? LineCount { get; set; }
}

// ── /files/read-batch ────────────────────────────────────────────────────────
public sealed class CmBatchReadItem
{
    public string Path { get; set; } = null!;
    public List<CmLineRange>? Ranges { get; set; }
    public long? MaxBytes { get; set; }
}

public sealed class CmBatchReadFilesRequest
{
    public List<CmBatchReadItem> Items { get; set; } = new();
    public bool IncludeLineNumbers { get; set; } = true;
    public bool NormalizeNewlines { get; set; } = true;
}

public sealed class CmBatchReadFilesResponse
{
    public List<CmReadFileResponse> Files { get; set; } = new();
}

// ── /files/write ─────────────────────────────────────────────────────────────
public sealed class CmWriteFileRequest
{
    public string Path { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? IfMatchEtag { get; set; }
    public string CreateMode { get; set; } = "createOrReplace";
    public string Encoding { get; set; } = "utf-8";
    public string Newline { get; set; } = "preserve";
    public bool MakeBackup { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public string? Reason { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class CmWriteFileResponse
{
    public string Path { get; set; } = null!;
    public string? Etag { get; set; }
    public long BytesWritten { get; set; }
    public bool Created { get; set; }
    public bool DryRun { get; set; }
    public string? BackupPath { get; set; }
}

// ── /files/delete ────────────────────────────────────────────────────────────
public sealed class CmDeleteFileRequest
{
    public string Path { get; set; } = null!;
    public bool Recursive { get; set; } = false;
    public bool SoftDelete { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public string? Confirm { get; set; }
    public string? Reason { get; set; }
}

public sealed class CmDeleteFileResponse
{
    public bool Deleted { get; set; }
    public string Path { get; set; } = null!;
    public string? BackupPath { get; set; }
    public bool DryRun { get; set; }
}

// ── /files/move ──────────────────────────────────────────────────────────────
public sealed class CmMoveFileRequest
{
    public string SourcePath { get; set; } = null!;
    public string DestinationPath { get; set; } = null!;
    public bool Overwrite { get; set; } = false;
    public bool UpdateReferences { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public string? Reason { get; set; }
}

public sealed class CmMoveFileResponse
{
    public bool Moved { get; set; }
    public string SourcePath { get; set; } = null!;
    public string DestinationPath { get; set; } = null!;
    public int UpdatedReferenceCount { get; set; }
    public bool DryRun { get; set; }
}

// ── /edits/apply-patch ───────────────────────────────────────────────────────
public sealed class CmPatchEdit
{
    public string Path { get; set; } = null!;
    public string? IfMatchEtag { get; set; }
    public string PatchType { get; set; } = null!;
    public string? UnifiedDiff { get; set; }
    public string? Search { get; set; }
    public string? Replace { get; set; }
    public bool IsRegex { get; set; } = false;
    public bool ReplaceAll { get; set; } = false;
    public CmLineRange? Range { get; set; }
}

public sealed class CmApplyPatchRequest
{
    public List<CmPatchEdit> Edits { get; set; } = new();
    public string TransactionMode { get; set; } = "allOrNothing";
    public bool ValidateSyntax { get; set; } = true;
    public bool MakeBackup { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public string? Reason { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class CmApplyPatchFileResult
{
    public string Path { get; set; } = null!;
    public string? Etag { get; set; }
    public bool Changed { get; set; }
    public string? BackupPath { get; set; }
    public string? Message { get; set; }
}

public sealed class CmApplyPatchResponse
{
    public bool Applied { get; set; }
    public bool DryRun { get; set; }
    public List<CmApplyPatchFileResult> Files { get; set; } = new();
    public List<CmDiagnostic> Diagnostics { get; set; } = new();
}

// ── /index/symbols/find ──────────────────────────────────────────────────────
public sealed class CmFindSymbolsRequest
{
    public string Query { get; set; } = null!;
    public string? Kind { get; set; }
    public string? Language { get; set; }
    public string? Namespace { get; set; }
    public string? Path { get; set; }
    public bool Fuzzy { get; set; } = true;
    public bool IncludeDeclarations { get; set; } = true;
    public bool IncludeDefinitions { get; set; } = true;
    public int MaxResults { get; set; } = 100;
}

public sealed class CmSymbolRef
{
    public string SymbolId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string? Language { get; set; }
    public string? Namespace { get; set; }
    public string? ContainerName { get; set; }
    public string Path { get; set; } = null!;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Signature { get; set; }
}

public sealed class CmFindSymbolsResponse
{
    public List<CmSymbolRef> Results { get; set; } = new();
}

// ── /index/references/find ───────────────────────────────────────────────────
public sealed class CmFindReferencesRequest
{
    public string SymbolId { get; set; } = null!;
    public string? Name { get; set; }
    public string? Path { get; set; }
    public bool IncludeDefinitions { get; set; } = false;
    public bool IncludeDeclarations { get; set; } = false;
    public int MaxResults { get; set; } = 500;
}

public sealed class CmReferenceResult
{
    public string Path { get; set; } = null!;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Preview { get; set; }
    public string? ReferenceKind { get; set; }
}

public sealed class CmFindReferencesResponse
{
    public List<CmReferenceResult> References { get; set; } = new();
}

// ── /index/dependencies ──────────────────────────────────────────────────────
public sealed class CmDependenciesRequest
{
    public string? Path { get; set; }
    public string? SymbolId { get; set; }
    public string Direction { get; set; } = "both";
    public int Depth { get; set; } = 2;
    public bool IncludeTransitive { get; set; } = true;
    public bool IncludeExternal { get; set; } = false;
}

public sealed class CmDependencyNode
{
    public string Id { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string? Path { get; set; }
}

public sealed class CmDependencyEdge
{
    public string From { get; set; } = null!;
    public string To { get; set; } = null!;
    public string? Kind { get; set; }
}

public sealed class CmDependenciesResponse
{
    public List<CmDependencyNode> Nodes { get; set; } = new();
    public List<CmDependencyEdge> Edges { get; set; } = new();
}

// ── /index/call-graph ────────────────────────────────────────────────────────
public sealed class CmCallGraphRequest
{
    public string SymbolId { get; set; } = null!;
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string Direction { get; set; } = "both";
    public int Depth { get; set; } = 2;
    public int MaxNodes { get; set; } = 200;
}

public sealed class CmCallEdge
{
    public string FromSymbolId { get; set; } = null!;
    public string ToSymbolId { get; set; } = null!;
}

public sealed class CmCallGraphResponse
{
    public CmSymbolRef Center { get; set; } = null!;
    public List<CmSymbolRef> Nodes { get; set; } = new();
    public List<CmCallEdge> Edges { get; set; } = new();
}

// ── /validation/build ────────────────────────────────────────────────────────
public sealed class CmBuildRequest
{
    public string? Target { get; set; }
    public string? Configuration { get; set; }
    public string? Framework { get; set; }
    public string? Runtime { get; set; }
    public bool NoRestore { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 600;
    public Dictionary<string, string>? Env { get; set; }
}

public sealed class CmDiagnostic
{
    public string Severity { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? Path { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public string? Source { get; set; }
}

public sealed class CmBuildResponse
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public List<CmDiagnostic> Diagnostics { get; set; } = new();
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public long DurationMs { get; set; }
}

// ── /validation/format ───────────────────────────────────────────────────────
public sealed class CmFormatRequest
{
    public List<string> Paths { get; set; } = new();
    public string? Language { get; set; }
    public bool Fix { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public string? Tool { get; set; }
    public string? Target { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class CmFormatResponse
{
    public List<string> FormattedFiles { get; set; } = new();
    public bool Changed { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
}

// ── /validation/test ─────────────────────────────────────────────────────────
public sealed class CmTestRequest
{
    public string? Target { get; set; }
    public string? Filter { get; set; }
    public bool CollectCoverage { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 900;
    public Dictionary<string, string>? Env { get; set; }
}

public sealed class CmTestCaseResult
{
    public string Name { get; set; } = null!;
    public string Outcome { get; set; } = null!;
    public long? DurationMs { get; set; }
    public string? Message { get; set; }
    public string? StackTrace { get; set; }
}

public sealed class CmTestSummary
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
}

public sealed class CmTestResponse
{
    public bool Success { get; set; }
    public CmTestSummary Summary { get; set; } = null!;
    public List<CmTestCaseResult> Tests { get; set; } = new();
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public long DurationMs { get; set; }
}

// ── /history/changes ─────────────────────────────────────────────────────────
public sealed class CmChangesRequest
{
    public string? BaseRef { get; set; }
    public bool IncludePatch { get; set; } = true;
    public bool IncludeUntracked { get; set; } = true;
    public int MaxFiles { get; set; } = 500;
}

public sealed class CmChangedFile
{
    public string Path { get; set; } = null!;
    public string? OldPath { get; set; }
    public string Status { get; set; } = null!;
    public string? Patch { get; set; }
}

public sealed class CmChangesResponse
{
    public List<CmChangedFile> Files { get; set; } = new();
}