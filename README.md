# StewardessMCPServive

A production-quality C# .NET Framework 4.7.2 MCP (Model Context Protocol) service that exposes a local source-code repository to an AI agent through a secure Web API and an MCP-compatible JSON-RPC 2.0 tool surface.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Project Structure](#project-structure)
3. [Prerequisites](#prerequisites)
4. [Quick Start](#quick-start)
5. [Configuration Reference](#configuration-reference)
6. [REST API Reference](#rest-api-reference)
7. [MCP Tool Reference](#mcp-tool-reference)
8. [Security Guide](#security-guide)
9. [Example Agent Workflows](#example-agent-workflows)
10. [Testing](#testing)
11. [Operational Guidance](#operational-guidance)
12. [Future Improvements](#future-improvements)

---

## Architecture Overview

```
AI Agent (OpenAI / custom client)
         │
         │  HTTP / JSON
         ▼
┌──────────────────────────────────────────────────────┐
│          StewardessMCPServive  (ASP.NET Web API + OWIN)       │
│                                                       │
│  ┌────────────────┐   ┌──────────────────────────┐   │
│  │ MCP Endpoint   │   │  REST Controllers         │   │
│  │ POST /mcp/v1/  │   │  /api/repository          │   │
│  │ JSON-RPC 2.0   │   │  /api/files               │   │
│  └───────┬────────┘   │  /api/search              │   │
│          │            │  /api/edit                │   │
│          └──────┬─────│  /api/git                 │   │
│                 │     │  /api/command             │   │
│                 │     └────────────┬──────────────┘   │
│                 │                  │                  │
│          ┌──────▼──────────────────▼───────────┐      │
│          │           Service Layer              │      │
│          │  IFileSystemService                  │      │
│          │  ISearchService                      │      │
│          │  IEditService                        │      │
│          │  IGitService                         │      │
│          │  ICommandService                     │      │
│          │  IProjectDetectionService            │      │
│          └──────────────┬───────────────────────┘      │
│                         │                              │
│  ┌──────────────────────▼──────────────────────┐       │
│  │        Cross-Cutting Concerns               │       │
│  │  PathValidator  (sandbox + traversal guard) │       │
│  │  IAuditService  (append-only audit trail)   │       │
│  │  ApiKeyAuthFilter  (auth enforcement)       │       │
│  │  RequestIdFilter   (correlation IDs)        │       │
│  │  GlobalExceptionHandler                     │       │
│  └─────────────────────────────────────────────┘       │
│                                                        │
│  Configuration: McpServiceSettings (Web.config)        │
└────────────────────────────────────────────────────────┘
         │
         ▼
   File System (sandboxed to RepositoryRoot)
```

The service has two entry points that expose the same capabilities:

- **REST API** — standard HTTP endpoints usable from any HTTP client or OpenAI function-calling tool definition.
- **MCP endpoint** (`POST /mcp/v1/`) — JSON-RPC 2.0 protocol compatible with MCP clients; supports `tools/list` and `tools/call`.

---

## Project Structure

```
StewardessMCPServive.sln
│
├── StewardessMCPServive/                      # Main web application project
│   ├── App_Start/
│   │   └── WebApiConfig.cs            # Route registration
│   ├── Configuration/
│   │   └── McpServiceSettings.cs      # Strongly-typed settings from Web.config
│   ├── Controllers/
│   │   ├── BaseController.cs          # Shared helpers and response builders
│   │   ├── CapabilitiesController.cs  # GET /api/capabilities
│   │   ├── CommandController.cs       # POST /api/command/build, /test, /run
│   │   ├── EditController.cs          # POST /api/edit/*
│   │   ├── FileController.cs          # GET /api/files/*
│   │   ├── GitController.cs           # GET/POST /api/git/*
│   │   ├── HealthController.cs        # GET /api/health
│   │   ├── McpController.cs           # POST /mcp/v1/  (JSON-RPC 2.0)
│   │   ├── RepositoryController.cs    # GET /api/repository/*
│   │   └── SearchController.cs        # POST /api/search/*
│   ├── Infrastructure/
│   │   ├── ApiKeyAuthFilter.cs        # X-Api-Key / Bearer token enforcement
│   │   ├── GlobalExceptionHandler.cs  # Unhandled exception → ApiResponse
│   │   ├── PathValidator.cs           # Sandbox + traversal prevention
│   │   ├── RequestIdFilter.cs         # Injects X-Request-Id correlation header
│   │   └── ServiceLocator.cs          # Simple singleton DI container
│   ├── Mcp/
│   │   ├── McpToolHandler.cs          # JSON-RPC 2.0 dispatcher
│   │   └── McpToolRegistry.cs         # 39 tool definitions + handlers
│   ├── Models/
│   │   ├── ApiResponse.cs             # Shared response envelope
│   │   ├── AuditModels.cs             # Audit log entry types
│   │   ├── CommandModels.cs           # Build / test / run DTOs
│   │   ├── EditModels.cs              # Write / patch / diff DTOs
│   │   ├── FileModels.cs              # Read / metadata DTOs
│   │   ├── GitModels.cs               # Git status / diff / log DTOs
│   │   ├── McpModels.cs               # JSON-RPC envelope types
│   │   ├── RepositoryModels.cs        # Directory listing / tree DTOs
│   │   └── SearchModels.cs            # Search request / result DTOs
│   ├── Services/
│   │   ├── AuditService.cs            # Append-only NDJSON audit logger
│   │   ├── CommandService.cs          # Build / test / shell execution
│   │   ├── EditService.cs             # Write / patch / diff operations
│   │   ├── FileSystemService.cs       # Read / metadata / encoding detection
│   │   ├── GitService.cs              # Git status / diff / log via CLI
│   │   ├── ProjectDetectionService.cs # Solution / project / NuGet detection
│   │   ├── SearchService.cs           # Text / regex / filename search
│   │   ├── SecurityService.cs         # IP allowlist, approval tokens
│   │   ├── IAuditService.cs
│   │   ├── ICommandService.cs
│   │   ├── IEditService.cs
│   │   ├── IFileSystemService.cs
│   │   ├── IGitService.cs
│   │   ├── IProjectDetectionService.cs
│   │   ├── ISearchService.cs
│   │   └── ISecurityService.cs
│   ├── Properties/
│   │   └── AssemblyInfo.cs
│   ├── Startup.cs                     # OWIN startup; DI wiring
│   └── Web.config                     # All Mcp:* configuration keys
│
└── StewardessMCPServive.Tests/                # xUnit test project (net472)
    ├── Helpers/
    │   └── TempRepository.cs          # Disposable temp-dir fixture
    └── Services/
        ├── CommandServiceTests.cs      # 19 tests
        ├── EditServiceTests.cs         # 37 tests
        ├── FileSystemServiceTests.cs   # (existing)
        ├── GitServiceTests.cs          # 14 tests
        └── SearchServiceTests.cs       # (existing)
```

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| Windows | The service runs as a local web application |
| .NET Framework 4.7.2 | Install via Visual Studio or standalone installer |
| Visual Studio 2019+ or MSBuild 16+ | Required to build |
| IIS Express (included with VS) | Or full IIS for production deployment |
| Git CLI | Required for git-related features; must be on `PATH` |
| `dotnet` CLI or MSBuild | Required for build/test features |

---

## Quick Start

### 1. Clone and build

```powershell
git clone <this-repo>
cd StewardessMCPServive
nuget restore StewardessMCPServive.sln
msbuild StewardessMCPServive.sln /p:Configuration=Release
```

Or open `StewardessMCPServive.sln` in Visual Studio and press **Build Solution**.

### 2. Configure the repository root

Edit `StewardessMCPServive\Web.config`:

```xml
<add key="Mcp:RepositoryRoot" value="C:\repos\your-project" />
```

Set this to the absolute path of the local repository you want the AI agent to work with.

### 3. (Optional) Set an API key

```xml
<add key="Mcp:ApiKey" value="your-secret-key" />
```

When non-empty, all requests must include either:
- Header: `X-Api-Key: your-secret-key`
- Header: `Authorization: Bearer your-secret-key`

### 4. Run the service

**IIS Express (Visual Studio):**
- Press F5 or Ctrl+F5. The service starts at `http://localhost:<port>/`.

**IIS:**
- Deploy to an IIS site targeting `net472`.
- Set the application pool to **No Managed Code** (OWIN manages the pipeline).

### 5. Verify

```powershell
curl http://localhost:5000/api/health
```

Expected response:
```json
{
  "success": true,
  "data": {
    "status": "Healthy",
    "version": "2.0.0",
    "repositoryRoot": "C:\\repos\\your-project",
    "readOnlyMode": false,
    "uptimeSeconds": 12
  }
}
```

---

## Configuration Reference

All settings live under `<appSettings>` in `Web.config` with the prefix `Mcp:`.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Mcp:RepositoryRoot` | string | *(required)* | Absolute path to the repository root. All file operations are sandboxed here. |
| `Mcp:ReadOnlyMode` | bool | `false` | When `true`, all write/edit/run operations are rejected with HTTP 403. |
| `Mcp:ApiKey` | string | *(empty)* | API key for authentication. Leave empty to disable auth. |
| `Mcp:AllowedIps` | CSV | `127.0.0.1,::1` | IP allowlist. Leave empty to allow all IPs. |
| `Mcp:MaxFileReadBytes` | long | `5242880` | Maximum bytes read for a single file (default 5 MB). |
| `Mcp:MaxSearchResults` | int | `200` | Maximum matches returned per search call. |
| `Mcp:MaxDirectoryDepth` | int | `10` | Maximum recursion depth for `list_tree`. |
| `Mcp:MaxCommandExecutionSeconds` | int | `120` | Timeout for build/test/run operations (seconds). |
| `Mcp:AllowedCommands` | CSV | `dotnet build,dotnet test,dotnet restore` | Prefix-matched list of allowed shell commands. |
| `Mcp:BlockedFolders` | CSV | `.git,bin,obj,.vs,packages,node_modules,.mcp` | Folder names excluded from navigation and search. |
| `Mcp:BlockedExtensions` | CSV | `.exe,.dll,.pdb,.suo,.user` | File extensions blocked from read and write. |
| `Mcp:AllowedWriteExtensions` | CSV | *(empty = all)* | When non-empty, only these extensions may be written. |
| `Mcp:ApprovalRequiredForDestructive` | bool | `false` | When `true`, delete and overwrite operations require a one-time approval token. |
| `Mcp:ApprovalTokenLifetimeSeconds` | int | `120` | How long (seconds) a pre-approval token remains valid. |
| `Mcp:EnableAuditLog` | bool | `true` | Enable append-only NDJSON audit trail. |
| `Mcp:AuditLogPath` | string | `<RepositoryRoot>/.mcp/audit.log` | Path for audit log file. |
| `Mcp:BackupDirectory` | string | `<RepositoryRoot>/.mcp/backups` | Directory for pre-edit file backups. |
| `Mcp:EnableBackups` | bool | `true` | Create a backup before every destructive edit. |
| `Mcp:MaxBackupsPerFile` | int | `10` | Maximum backup snapshots kept per file. |
| `Mcp:MaxFileSizeForWrite` | long | `10485760` | Maximum bytes for a single write (default 10 MB). |
| `Mcp:MaxDirectoryEntries` | int | `500` | Maximum entries returned for a single directory listing. |
| `Mcp:ServiceVersion` | string | `2.0.0` | Version string included in health and capabilities responses. |

---

## REST API Reference

All endpoints return an `ApiResponse<T>` envelope:

```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "requestId": "a1b2c3d4",
  "timestamp": "2024-01-15T12:00:00Z"
}
```

On failure:
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "FILE_NOT_FOUND",
    "message": "File not found: src/Foo.cs",
    "details": null
  },
  "requestId": "a1b2c3d4",
  "timestamp": "2024-01-15T12:00:00Z"
}
```

### Health & Diagnostics

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/health` | Service health, version, uptime |
| `GET` | `/api/capabilities` | All available tools and endpoint listing |
| `GET` | `/api/capabilities/version` | Version info |

### Repository Navigation

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/repository/info` | Repository root, settings summary |
| `GET` | `/api/repository/list?path=&includeBlocked=false` | List immediate children of a directory |
| `GET` | `/api/repository/tree?path=&depth=3` | Recursive directory tree |
| `GET` | `/api/repository/exists?path=` | Check if file or directory exists |
| `GET` | `/api/repository/metadata?path=` | File/directory metadata (size, dates, attributes) |
| `GET` | `/api/repository/projects` | Detect solution and project files |

### File Reading

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/files/read?path=&maxBytes=&returnBase64=false` | Read file content |
| `GET` | `/api/files/read-range?path=&startLine=&endLine=` | Read specific line range |
| `POST` | `/api/files/read-multiple` | Read multiple files in one call |
| `GET` | `/api/files/hash?path=` | SHA-256 hash of file |
| `GET` | `/api/files/encoding?path=` | Detect encoding and line endings |

### Search

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/search/text` | Full-text search with optional filters |
| `POST` | `/api/search/regex` | Regex search across repository |
| `POST` | `/api/search/filenames` | Find files by name pattern |
| `POST` | `/api/search/extensions` | Find all files by extension |
| `POST` | `/api/search/symbol` | Heuristic symbol/class/method name search |
| `POST` | `/api/search/references` | Heuristic reference/usage search |

#### Text search request body
```json
{
  "query": "MyClass",
  "path": "src",
  "extensions": [".cs"],
  "caseSensitive": false,
  "maxResults": 50,
  "includeContext": true,
  "contextLines": 2
}
```

### Edit & Write

All write operations are blocked when `ReadOnlyMode = true`.  
`dryRun: true` returns a preview without modifying any files.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/edit/write-file` | Write (overwrite) file |
| `POST` | `/api/edit/create-file` | Create new file (fails if exists) |
| `POST` | `/api/edit/append-file` | Append content to file |
| `POST` | `/api/edit/replace-text` | Replace exact text fragment |
| `POST` | `/api/edit/replace-lines` | Replace a range of lines |
| `POST` | `/api/edit/patch-file` | Apply unified diff patch |
| `POST` | `/api/edit/apply-diff` | Apply unified diff (alias) |
| `POST` | `/api/edit/batch-edits` | Apply multiple edits atomically |
| `POST` | `/api/edit/rename-path` | Rename file or directory |
| `POST` | `/api/edit/move-path` | Move file or directory |
| `POST` | `/api/edit/delete-file` | Delete a file (with backup) |
| `POST` | `/api/edit/delete-directory` | Delete directory (recursive optional) |
| `POST` | `/api/edit/create-directory` | Create directory |
| `POST` | `/api/edit/rollback` | Roll back to pre-edit backup snapshot |

#### Write file request body
```json
{
  "path": "src/MyClass.cs",
  "content": "public class MyClass { }",
  "dryRun": false,
  "createBackup": true
}
```

#### Patch file request body
```json
{
  "path": "src/MyClass.cs",
  "patch": "--- a/src/MyClass.cs\n+++ b/src/MyClass.cs\n@@ -1,1 +1,1 @@\n-old line\n+new line\n",
  "dryRun": false
}
```

### Git

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/git/repo` | Check if path is a git repository |
| `GET` | `/api/git/status?path=` | Git status (porcelain) |
| `POST` | `/api/git/diff` | Full diff (working tree or staged) |
| `GET` | `/api/git/diff/file?path=&scope=working` | Diff for a specific file |
| `POST` | `/api/git/log` | Git log with filters |

### Build & Command

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/command/allowed` | List allowed commands |
| `POST` | `/api/command/build` | Run build command |
| `POST` | `/api/command/test` | Run test command |
| `POST` | `/api/command/run` | Run any allowed custom command |

#### Build request body
```json
{
  "projectPath": "MyApp/MyApp.csproj",
  "configuration": "Release",
  "extraArgs": "/p:TreatWarningsAsErrors=true",
  "timeoutSeconds": 120
}
```

#### Run command request body
```json
{
  "command": "dotnet restore",
  "workingDirectory": "",
  "timeoutSeconds": 60
}
```

### MCP Endpoint (JSON-RPC 2.0)

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/mcp/v1/` | JSON-RPC 2.0 dispatch endpoint |
| `GET` | `/mcp/v1/tools` | Convenience — same as `tools/list` |
| `GET` | `/mcp/v1/manifest` | Full capabilities manifest |

#### JSON-RPC request format
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "tools/call",
  "params": {
    "name": "read_file",
    "arguments": {
      "path": "src/Program.cs",
      "maxBytes": 65536
    }
  }
}
```

#### JSON-RPC response format
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{ \"path\": \"src/Program.cs\", \"content\": \"...\" }"
      }
    ]
  }
}
```

---

## MCP Tool Reference

All 39 tools are available via `POST /mcp/v1/` and `GET /mcp/v1/tools`.

### Repository (6 tools)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `get_repository_info` | Returns root path, settings, git status | — |
| `list_directory` | List directory contents | `path`, `includeBlocked` |
| `list_tree` | Recursive directory tree | `path`, `depth` (max 10) |
| `file_exists` | Check if file exists | `path` |
| `directory_exists` | Check if directory exists | `path` |
| `get_metadata` | File/directory metadata | `path` |

### Search (6 tools)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `search_text` | Full-text search | `query`, `path`, `extensions`, `caseSensitive`, `maxResults` |
| `search_regex` | Regex search | `pattern`, `path`, `extensions`, `maxResults` |
| `search_file_names` | Find files by name pattern | `pattern`, `path` |
| `search_by_extension` | Find all files of given extension | `extension`, `path` |
| `search_symbol` | Heuristic symbol/type name search | `symbolName`, `path` |
| `find_references` | Heuristic identifier usage search | `identifierName`, `path` |

### Files (5 tools)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `read_file` | Read file content | `path`, `maxBytes`, `returnBase64` |
| `read_file_range` | Read line range | `path`, `startLine`, `endLine` |
| `read_multiple_files` | Read multiple files | `paths` (array) |
| `get_file_hash` | SHA-256 hash | `path` |
| `detect_encoding` | Detect encoding and line endings | `path` |

### Edit (14 tools)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `write_file` | Write (overwrite) file | `path`, `content`, `dryRun` |
| `create_file` | Create new file | `path`, `content`, `dryRun` |
| `append_file` | Append to file | `path`, `content`, `dryRun` |
| `replace_text` | Replace text fragment | `path`, `oldText`, `newText`, `dryRun` |
| `replace_lines` | Replace line range | `path`, `startLine`, `endLine`, `newContent`, `dryRun` |
| `patch_file` | Apply unified diff | `path`, `patch`, `dryRun` |
| `apply_unified_diff` | Apply unified diff (alias) | `path`, `patch`, `dryRun` |
| `batch_edits` | Multiple edits atomically | `edits` (array), `dryRun` |
| `rename_path` | Rename file/directory | `path`, `newName`, `dryRun` |
| `move_path` | Move file/directory | `path`, `destination`, `dryRun` |
| `delete_file` | Delete file | `path`, `dryRun` |
| `delete_directory` | Delete directory | `path`, `recursive`, `dryRun` |
| `create_directory` | Create directory | `path` |
| `rollback_change` | Rollback to backup | `rollbackToken` |

### Git (4 tools)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `get_git_status` | Git status | `path` |
| `get_git_diff` | Full diff | `scope` (working/staged/head), `unified` |
| `get_git_diff_for_file` | Diff for single file | `path`, `scope` |
| `get_git_log` | Git log | `path`, `maxEntries`, `since`, `until`, `author` |

### Command (3 tools)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `run_build` | Run build | `projectPath`, `configuration`, `extraArgs`, `timeoutSeconds` |
| `run_tests` | Run tests | `projectPath`, `filter`, `extraArgs`, `timeoutSeconds` |
| `run_custom_command` | Run allowed command | `command`, `workingDirectory`, `timeoutSeconds` |

---

## Security Guide

### Threat model

| Threat | Mitigation |
|--------|-----------|
| Path traversal | `PathValidator` rejects any resolved path outside `RepositoryRoot` |
| Unauthenticated access | `ApiKeyAuthFilter` enforces `X-Api-Key` / Bearer token when configured |
| Arbitrary shell execution | `AllowedCommands` prefix allowlist; `shell = false` (no `cmd.exe`) |
| Large file / DoS | `MaxFileReadBytes`, `MaxSearchResults`, `MaxCommandExecutionSeconds` limits |
| Write to system files | `ReadOnlyMode`; sandbox enforced on every write path |
| Dangerous extension write | `BlockedExtensions` denylist; optional `AllowedWriteExtensions` allowlist |
| Audit evasion | `AuditService` appends before and after every mutation |
| Accidental data loss | Automatic backup before every destructive edit; rollback token returned |

### Recommended production settings

```xml
<!-- Web.config for production/shared use -->
<add key="Mcp:ApiKey"                          value="YOUR-STRONG-SECRET" />
<add key="Mcp:AllowedIps"                      value="127.0.0.1,::1" />
<add key="Mcp:ReadOnlyMode"                    value="false" />
<add key="Mcp:ApprovalRequiredForDestructive"  value="true" />
<add key="Mcp:EnableAuditLog"                  value="true" />
<add key="Mcp:EnableBackups"                   value="true" />
<add key="Mcp:AllowedCommands"                 value="dotnet build,dotnet test,dotnet restore,git status,git log,git diff,git show" />
<add key="Mcp:BlockedExtensions"               value=".exe,.dll,.pdb,.suo,.user,.pfx,.key,.p12,.cer" />
```

### ⚠️ Important warnings

- **Do not expose this service publicly** without a VPN, reverse proxy with TLS, and strong API key authentication.
- The `run_custom_command` / `run_build` / `run_tests` tools execute real processes. Keep `AllowedCommands` as narrow as possible.
- The audit log (`audit.log`) records every mutating operation. Back it up periodically.
- `ReadOnlyMode = true` is the safest configuration for read-and-analyze workflows.

---

## Example Agent Workflows

### Workflow 1: Find, inspect, patch, build

An AI agent fixes a bug in a C# class.

```json
// Step 1 — Find the class
{ "method": "tools/call", "params": { "name": "search_symbol", "arguments": { "symbolName": "OrderProcessor" } } }

// Step 2 — Read the file
{ "method": "tools/call", "params": { "name": "read_file", "arguments": { "path": "src/Orders/OrderProcessor.cs" } } }

// Step 3 — Preview the patch
{ "method": "tools/call", "params": { "name": "patch_file", "arguments": {
  "path": "src/Orders/OrderProcessor.cs",
  "patch": "--- a/src/Orders/OrderProcessor.cs\n+++ b/src/Orders/OrderProcessor.cs\n@@ -42,7 +42,7 @@\n-        if (order == null) throw new Exception(\"null\");\n+        if (order == null) throw new ArgumentNullException(nameof(order));\n",
  "dryRun": true
}}}

// Step 4 — Apply the patch
{ "method": "tools/call", "params": { "name": "patch_file", "arguments": {
  "path": "src/Orders/OrderProcessor.cs",
  "patch": "...",
  "dryRun": false
}}}

// Step 5 — Run build to verify
{ "method": "tools/call", "params": { "name": "run_build", "arguments": { "configuration": "Debug" } } }

// Step 6 — Check diff
{ "method": "tools/call", "params": { "name": "get_git_diff", "arguments": { "scope": "working" } } }
```

### Workflow 2: Multi-file refactor with test run

An AI agent renames a method across all files.

```json
// Step 1 — Find all usages
{ "method": "tools/call", "params": { "name": "find_references", "arguments": { "identifierName": "ProcessOrder", "extensions": [".cs"] } } }

// Step 2 — Replace in each file (dry-run first)
{ "method": "tools/call", "params": { "name": "replace_text", "arguments": {
  "path": "src/Orders/OrderProcessor.cs",
  "oldText": "ProcessOrder",
  "newText": "ExecuteOrder",
  "dryRun": true
}}}

// Step 3 — Apply to all affected files
{ "method": "tools/call", "params": { "name": "batch_edits", "arguments": {
  "edits": [
    { "path": "src/Orders/OrderProcessor.cs", "operation": "replace_text", "oldText": "ProcessOrder", "newText": "ExecuteOrder" },
    { "path": "src/Orders/OrderService.cs",   "operation": "replace_text", "oldText": "ProcessOrder", "newText": "ExecuteOrder" },
    { "path": "tests/OrderProcessorTests.cs", "operation": "replace_text", "oldText": "ProcessOrder", "newText": "ExecuteOrder" }
  ],
  "dryRun": false
}}}

// Step 4 — Run tests
{ "method": "tools/call", "params": { "name": "run_tests", "arguments": { "projectPath": "tests/MyApp.Tests.csproj" } } }

// Step 5 — Rollback if tests fail (use rollbackToken from batch_edits response)
{ "method": "tools/call", "params": { "name": "rollback_change", "arguments": { "rollbackToken": "backup_20240115_120000_OrderProcessor.cs" } } }
```

### Workflow 3: Architecture inspection

An AI agent analyses the project structure and suggests improvements.

```json
// Step 1 — Get full tree
{ "method": "tools/call", "params": { "name": "list_tree", "arguments": { "depth": 4 } } }

// Step 2 — Detect solution and project files
{ "method": "tools/call", "params": { "name": "get_repository_info", "arguments": {} } }

// Step 3 — Read key config files
{ "method": "tools/call", "params": { "name": "read_multiple_files", "arguments": { "paths": ["App.config", "appsettings.json", "Web.config"] } } }

// Step 4 — Find all interfaces
{ "method": "tools/call", "params": { "name": "search_regex", "arguments": { "pattern": "^\\s*public interface I\\w+", "extensions": [".cs"] } } }

// Step 5 — Search for known anti-patterns
{ "method": "tools/call", "params": { "name": "search_text", "arguments": { "query": "catch (Exception)", "extensions": [".cs"] } } }
```

### Workflow 4: Safe batch refactor with preview

An AI agent updates a namespace across the project.

```json
// Step 1 — Find all files that use the old namespace
{ "method": "tools/call", "params": { "name": "search_text", "arguments": { "query": "namespace OldCompany.Orders", "extensions": [".cs"] } } }

// Step 2 — Preview changes in first file
{ "method": "tools/call", "params": { "name": "replace_text", "arguments": {
  "path": "src/Orders/OrderProcessor.cs",
  "oldText": "namespace OldCompany.Orders",
  "newText": "namespace NewCompany.Orders",
  "dryRun": true
}}}

// Step 3 — Apply all changes
{ "method": "tools/call", "params": { "name": "batch_edits", "arguments": {
  "edits": [ ... all files ... ],
  "dryRun": false
}}}

// Step 4 — Build to verify
{ "method": "tools/call", "params": { "name": "run_build", "arguments": {} } }
```

### Workflow 5: Rollback after failed modification

```json
// Step 1 — Apply a risky change (note the rollbackToken in response)
{ "method": "tools/call", "params": { "name": "write_file", "arguments": {
  "path": "src/Config/AppSettings.cs",
  "content": "...",
  "createBackup": true
}}}
// Response includes: "rollbackToken": "20240115_120500_AppSettings.cs"

// Step 2 — Build fails
{ "method": "tools/call", "params": { "name": "run_build", "arguments": {} } }
// Response: "ErrorCount": 5

// Step 3 — Rollback
{ "method": "tools/call", "params": { "name": "rollback_change", "arguments": {
  "rollbackToken": "20240115_120500_AppSettings.cs"
}}}
```

---

## Testing

### Run all tests

```powershell
# Build first
msbuild StewardessMCPServive.sln /p:Configuration=Debug

# Run tests
& "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" `
    "StewardessMCPServive.Tests\bin\Debug\StewardessMCPServive.Tests.dll" `
    /TestAdapterPath:"packages\xunit.runner.visualstudio.2.5.3\build\net462" `
    /Framework:net472
```

Expected output: **143 tests, 143 passed**.

### Test coverage

| Test class | Tests | Coverage area |
|-----------|-------|---------------|
| `FileSystemServiceTests` | ~30 | File read, metadata, encoding, paging |
| `SearchServiceTests` | ~20 | Text/regex/filename/extension search |
| `EditServiceTests` | 37 | All write/patch/diff/rollback paths |
| `GitServiceTests` | 14 | Git status/diff/log parsing |
| `CommandServiceTests` | 19 | Allowlist, parse summaries, process execution |

---

## Operational Guidance

### Audit log

When `Mcp:EnableAuditLog = true`, every mutating operation is appended to the audit log as a single NDJSON line:

```json
{"timestamp":"2024-01-15T12:00:00Z","requestId":"a1b2c3d4","operationType":"WriteFile","path":"src/Program.cs","actor":"127.0.0.1","outcome":"Success","details":"Bytes written: 512"}
```

The audit log is at `<RepositoryRoot>/.mcp/audit.log` by default.

### Backup snapshots

Before every destructive write, the original file is copied to:
```
<BackupDirectory>/<relative-path>/<yyyyMMdd_HHmmss>_<filename>
```

The response includes a `rollbackToken` that can be passed to `rollback_change` / `POST /api/edit/rollback`.

### Connecting an OpenAI function-calling agent

1. Call `GET /mcp/v1/manifest` to get the full capabilities manifest.
2. Convert the JSON Schema tool definitions from `GET /mcp/v1/tools` into OpenAI function definitions.
3. For each tool call the model makes, POST to `POST /mcp/v1/` with the JSON-RPC 2.0 envelope.
4. Return the `result.content[0].text` value back to the model as the tool result.

Alternatively, use the REST endpoints directly — every MCP tool maps 1:1 to a REST endpoint.

### Connecting via a standard MCP client

The service exposes a JSON-RPC 2.0 endpoint at `POST /mcp/v1/` that implements:
- `ping` — health check
- `tools/list` — enumerate all available tools with JSON Schema
- `tools/call` — invoke a tool by name with structured arguments

Any MCP-compatible client (Claude Desktop, Continue.dev, etc.) can connect by pointing at `http://localhost:<port>/mcp/v1/`.

---

## Future Improvements

1. **WebSocket / SSE transport** — Add a streaming MCP transport so agents receive incremental output from long-running build/test commands.
2. **In-memory search index** — Build a Roslyn-based or lightweight inverted index at startup for faster symbol and reference lookups.
3. **Semantic search** — Integrate a local embedding model (e.g., via ONNX Runtime) for semantic code search beyond text matching.
4. **Multi-repo support** — Allow multiple named repository roots, each with independent policy settings.
5. **Git write operations** — Expose `git add`, `git commit`, `git checkout -b` through the allowlist mechanism.
6. **Rate limiting** — Add per-IP token-bucket middleware for public deployments.
7. **OpenAPI / Swagger UI** — Wire up Swashbuckle for a browser-accessible API explorer.
8. **Cancellation over HTTP** — Surface `CancellationToken` cancellation to the client via DELETE on the request ID.
9. **Configuration hot-reload** — Watch `Web.config` for changes without requiring a restart.
10. **Signed audit log** — Add HMAC signatures to audit log entries to detect tampering.
