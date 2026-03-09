# Stewardess MCP Service

**A local-first MCP server that gives AI agents safe, auditable access to your codebase.**

Stewardess lets an AI agent **read, search, understand, edit, build, test, inspect Git history, and semantically analyze** a repository on your machine — without sending your code to the cloud.

It is designed for developers who want a **real coding assistant**, not just a chat window that guesses based on pasted snippets.

---

## Why Stewardess

Most AI coding tools only see what you manually paste into a prompt. Stewardess changes that.

It acts as a controlled bridge between an AI agent and a repository on your machine. Once connected, the agent can:

- navigate the repository structure
- search across files and symbols
- read file contents safely
- apply precise edits
- run approved build and test commands
- inspect Git state and history
- build a semantic code index for deeper reasoning

You stay in control through:

- repository sandboxing
- read-only mode
- command allow-lists
- approval workflows for destructive actions
- audit logging
- backups and rollback
- configurable size and execution limits

Stewardess is especially well suited for a **fully self-hosted AI coding stack** using:

- **Ollama** for local model inference
- **Open WebUI** as the chat interface
- **MCP** as the tool-calling protocol

That means you can run an AI coding assistant with:

- no subscription fees
- no cloud dependency
- no code leaving your machine

---

## What This Project Is

Stewardess is a **local MCP service** for repository access and code intelligence.

It exposes a set of structured tools over **Model Context Protocol (MCP)** so that any MCP-capable AI client can interact with a repository safely and consistently.

In practice, Stewardess gives an AI agent a toolbox for:

- repository exploration
- text search
- file reading
- code-structure discovery
- file editing
- Git inspection
- controlled command execution
- semantic indexing and dependency analysis

---

## Who It Is For

Stewardess is built for:

- developers who want a local AI coding assistant that can actually see the whole repository
- teams who want AI-assisted review, refactoring, documentation, or test generation with guardrails
- self-hosters who want a private alternative to cloud coding agents
- anyone building AI-agent workflows around code, repositories, or developer tooling

---

## Core Capabilities

Stewardess currently provides tools in these areas:

### Repository navigation
Understand the layout of a project without opening every file manually.

### Search
Find text, regex matches, filenames, extensions, symbols, and references.

### File reading
Read files safely with size limits and line-range access.

### File editing
Create, modify, patch, move, rename, or delete files with backup and rollback support.

### Git inspection
Read repository status, diffs, commit history, and individual commit details.

### Command execution
Run only approved build, test, or custom commands from an explicit allow-list.

### Semantic code indexing
Build a code index and query symbols, members, occurrences, dependencies, dependents, and file relationships.

---

## Tool Model

Stewardess exposes tools in two broad classes:

### 1. Heuristic / filesystem tools
These work immediately and do not require semantic indexing.

Examples:
- repository listing
- text search
- file read
- file write
- Git queries
- build/test command execution

### 2. Semantic code-index tools
These require an index to be built first, but they provide much better precision.

Examples:
- symbol search
- symbol lookup
- type member inspection
- namespace tree
- references
- dependencies
- dependents
- file-level dependency analysis

This split is important:

- the basic tools work everywhere
- the indexed tools are more precise and more useful for advanced agent workflows

---

## Supported Semantic Languages

The semantic code index currently supports:

- **C#** — namespaces, classes, interfaces, enums, delegates, methods, properties, fields, events, and reference extraction
- **Python** — modules, classes, functions, and import extraction

Other files can still be read, searched, and edited using the non-indexed tools.


### Repository Navigation

These tools let an agent understand the layout of your project without reading every file.

| Tool | What It Does |
|------|--------------|
| `get_repository_info` | Returns the repository name, root path, and basic file counts |
| `list_directory` | Lists the immediate files and folders inside a given directory |
| `list_tree` | Returns a recursive folder tree up to a configurable depth |
| `path_exists` | Checks whether a given file or folder path actually exists |
| `get_metadata` | Returns size, dates, encoding, and line count for a file or folder |
| `detect_encoding` | Detects the character encoding of a file (UTF-8, UTF-16, ASCII, etc.) |

---

### Search

These tools let an agent find things in your code without knowing the exact file.

| Tool | What It Does |
|------|--------------|
| `search_text` | Finds a literal string across all files; returns file paths, line numbers, and surrounding text |
| `search_regex` | Same as search_text but uses a regular expression pattern |
| `search_file_names` | Finds files whose names match a given word or wildcard |
| `search_by_extension` | Returns all files matching one or more file extensions (e.g., `.cs`, `.py`) |
| `search_symbol` | Searches for class, method, or interface declarations by name using text heuristics |
| `find_references` | Finds all textual usages of an identifier across the repository |

---

### File Reading

These tools let an agent read file contents safely, with automatic size limits.

| Tool | What It Does |
|------|--------------|
| `read_file` | Reads the full content of a file (automatically truncated if it exceeds the size limit) |
| `read_file_range` | Reads a specific range of lines from a file, useful for large files |
| `read_multiple_files` | Reads several files in one call; returns each file's content or a per-file error |
| `get_file_hash` | Returns a checksum (SHA-256 by default) of a file's contents for integrity checking |

---

### Code Intelligence (Structure Parsing)

These tools extract the logical structure of a code file without reading raw text.

| Tool | What It Does |
|------|--------------|
| `get_file_structure` | Returns a structural summary of a code file: namespaces, types, and methods (uses text heuristics, not a full compiler) |

---

### File Editing

These tools allow an agent to create, modify, or delete files. All modifications are backed up by default and can be rolled back.

| Tool | What It Does |
|------|--------------|
| `write_file` | Overwrites or creates a file with new content; supports dry-run and backup |
| `create_file` | Creates a new file; fails if the file already exists (unless overwrite is allowed) |
| `create_directory` | Creates a directory and any missing parent directories |
| `rename_path` | Renames a file or folder (keeps it in the same parent directory) |
| `move_path` | Moves a file or folder to a different location within the repository |
| `delete_file` | Deletes a file; creates a backup first so it can be restored |
| `delete_directory` | Deletes a directory; requires explicit confirmation for non-empty directories |
| `append_file` | Adds content to the end of an existing file |
| `replace_text` | Replaces all occurrences of a literal string inside a file |
| `replace_lines` | Replaces a specific range of line numbers with new content |
| `patch_file` | Applies a unified diff patch to a single file |
| `apply_diff` | Applies a multi-file unified diff in a single operation |
| `batch_edit` | Executes multiple different edits atomically — if any fail, all are rolled back |
| `preview_changes` | Dry-runs a batch of edits and returns a preview diff plus an approval token |
| `rollback` | Restores a file from a backup created by a previous operation |

---

### Git Operations

These tools give an agent read-only access to the Git history and working-tree state.

| Tool | What It Does |
|------|--------------|
| `get_git_status` | Returns the current branch, HEAD commit, and the list of changed/staged/untracked files |
| `get_git_diff` | Returns the unified diff for all working-tree or staged changes |
| `get_git_diff_file` | Returns the unified diff for a single file |
| `get_git_log` | Returns the commit history for the repository or a sub-path |
| `get_commit` | Returns the full details of a single commit (author, message, changed files, optionally the patch) |

---

### Command Execution

These tools allow an agent to run approved build or test commands. Only commands in the configured allow-list can be executed.

| Tool | What It Does |
|------|--------------|
| `run_build` | Runs the configured build command (e.g., `dotnet build`) |
| `run_tests` | Runs the configured test command (e.g., `dotnet test`) |
| `run_command` | Runs any custom command from the allowed-commands list |

---

### Code Index (Semantic Analysis)

These tools require you to first build an index of the repository. Once indexed, they provide semantic understanding of symbols, types, dependencies, and references — far more precise than text search.

**Index Management**

| Tool | What It Does |
|------|--------------|
| `code_index.build` | Parses all eligible source files in the repository and builds a structural index in memory |
| `code_index.update` | Re-parses only the files that have changed since the last index build |
| `code_index.get_index_status` | Reports the current state of the index: ready, building, stale, or empty |
| `code_index.clear_repository` | Removes all index data for a repository (cannot be undone; re-indexing will be needed) |
| `code_index.list_repositories` | Lists all repositories that have been indexed |
| `code_index.get_language_capabilities` | Lists the programming languages supported and what each language adapter can do |
| `code_index.ping` | Health check for the code index service |

**File and Snapshot Information**

| Tool | What It Does |
|------|--------------|
| `code_index.list_files` | Returns a paginated list of indexed files with language and parse status |
| `code_index.get_file_outline` | Returns the structural outline of a file: namespaces, classes, methods, with line numbers |
| `code_index.get_snapshot_info` | Returns statistics about the latest index snapshot: file counts, language breakdown, build times |

**Symbol Search and Retrieval**

| Tool | What It Does |
|------|--------------|
| `code_index.find_symbols` | Searches the symbol index by name, supporting exact, prefix, or contains matching |
| `code_index.get_symbol` | Returns full details for a single symbol (kind, qualified name, location, members) |
| `code_index.get_symbol_occurrences` | Returns every location where a symbol appears across the indexed files |
| `code_index.get_symbol_children` | Returns the direct child symbols of a parent (e.g., members of a class) |
| `code_index.get_type_members` | Returns the members of a type grouped by kind: constructors, methods, properties, fields |
| `code_index.resolve_location` | Resolves a symbol ID to a concrete file path, line number, and column |
| `code_index.get_namespace_tree` | Returns a tree of all namespaces and modules in the repository |

**Dependencies and References**

| Tool | What It Does |
|------|--------------|
| `code_index.get_imports` | Returns the import or using directives at the top of a file |
| `code_index.get_references` | Returns what a symbol depends on and what depends on it |
| `code_index.get_file_references` | Returns all reference edges originating from a specific file |
| `code_index.get_dependencies` | Returns the outbound dependencies of a symbol with high confidence |
| `code_index.get_dependents` | Returns which other symbols depend on a given symbol |
| `code_index.get_symbol_relationships` | Returns a combined view of children, references, dependencies, and dependents in one call |
| `code_index.get_file_dependencies` | Returns a file-level dependency projection showing which files a given file depends on |

---

## Supported Languages for Code Indexing

The semantic code index currently supports:

- **C#** — Full declaration parsing: namespaces, classes, interfaces, enums, delegates, methods, properties, fields, events. Reference extraction at declaration, base-type, and import levels.
- **Python** — Declaration parsing: modules, classes, functions, import extraction.

Other file types (JavaScript, TypeScript, etc.) can still be read and searched with the text-based tools; they just cannot be semantically indexed.

---

## Setup and Installation

### Prerequisites

- **.NET 8 SDK** — Download from https://dotnet.microsoft.com/download/dotnet/8
- **Git** — Required if you want to use the Git tools
- **Windows or Linux** — Both are supported

### Build the Service

From the root of the repository:

```
dotnet build Stewardess.sln
```

### Run the Service

```
cd StewardessMCPService.Core
dotnet run
```

The service starts on **http://localhost:55703**.

You can verify it is running by opening http://localhost:55703/api/capabilities in a browser — you should see a JSON document listing all 74 tools.

### Run the Tests

```
dotnet test Stewardess.sln
```

All 578 tests should pass.

---

## Configuration

All configuration lives in one file:

```
StewardessMCPService.Core\appsettings.json
```

The only required setting is `RepositoryRoot`. Everything else has a sensible default.

### Minimal Configuration

```json
{
  "Mcp": {
    "RepositoryRoot": "C:\\Projects\\MyProject"
  }
}
```

### Full Configuration Reference

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `RepositoryRoot` | text | **required** | Absolute path to the repository the AI agent will work with. All file operations are sandboxed to this folder. |
| `ReadOnlyMode` | true/false | `false` | When true, all write, edit, delete, and command operations are rejected. Use this when you want the AI to read and analyse only. |
| `ApiKey` | text | *(empty)* | If set, the service requires this key in every request. Leave empty to disable authentication. |
| `AllowedIPs` | comma list | *(all)* | Comma-separated list of IP addresses allowed to connect. Leave empty to allow all. |
| `RequireApprovalForDestructive` | true/false | `false` | When true, destructive operations require a confirmation token from a preview step. |
| `MaxFileReadBytes` | number | `5242880` (5 MB) | Maximum bytes that can be read from a single file. |
| `MaxFileSizeForWrite` | number | `10485760` (10 MB) | Maximum bytes that can be written in a single operation. |
| `MaxSearchResults` | number | `200` | Maximum number of search results returned per query. |
| `MaxDirectoryDepth` | number | `10` | Maximum folder depth for recursive tree listings. |
| `MaxCommandExecutionSeconds` | number | `60` | Timeout in seconds for build, test, and command operations. |
| `MaxDirectoryEntries` | number | `500` | Maximum number of entries returned in a single directory listing. |
| `BlockedFolders` | comma list | `.git,bin,obj,.vs,packages,node_modules` | Folder names that are always excluded from all operations. |
| `AllowedExtensions` | comma list | *(all)* | If set, only files with these extensions can be read or written. |
| `BlockedExtensions` | comma list | `.exe,.dll,.zip,...` | File extensions that are always refused. |
| `AllowedCommands` | comma list | `dotnet build,dotnet test,...` | Command prefixes the AI is allowed to execute. See the Commands section below. |
| `EnableAuditLog` | true/false | `true` | When true, every modifying operation is logged to the audit log. |
| `AuditLogPath` | text | *(default location)* | Path to the audit log file. |
| `EnableBackups` | true/false | `true` | When true, a backup is made of each file before it is modified. |
| `BackupDirectory` | text | *(default location)* | Directory where backup files are stored. |
| `MaxBackupsPerFile` | number | `10` | Maximum number of backup versions kept per file. |
| `LogLevel` | text | `Info` | Log verbosity: `Trace`, `Debug`, `Info`, `Warn`, or `Error`. |
| `ServiceVersion` | text | `2.0.0` | Version string returned in health and capabilities responses. |

### Configuration via Environment Variables

Any setting can be overridden with an environment variable. Replace the section separator with a double underscore and prefix with `MCP__`.

Examples:
```
MCP__REPOSITORYROOT=C:\Projects\MyProject
MCP__APIKEY=my-secret-key
MCP__READONLYMODE=true
MCP__ALLOWEDCOMMANDS=dotnet build,dotnet test
```

---

## Authentication

By default, authentication is disabled. To enable it, set an API key in `appsettings.json`:

```json
{
  "Mcp": {
    "ApiKey": "choose-a-strong-secret-here"
  }
}
```

Once set, every request to the service must include the key in one of two ways:

- As a header: `X-API-Key: choose-a-strong-secret-here`
- As a bearer token: `Authorization: Bearer choose-a-strong-secret-here`

The following endpoints are always accessible without authentication:

- `GET /api/capabilities` — Tool manifest and schema
- `GET /api/tools` — Tool list
- `GET /api/health` — Health check

---

## Configuring Allowed Commands

The `run_build`, `run_tests`, and `run_command` tools only execute commands that match entries in the `AllowedCommands` list. Matching is done by case-insensitive prefix — so `"dotnet"` would allow `dotnet build`, `dotnet test`, `dotnet restore`, and so on.

Default allowed commands:
- `dotnet build`
- `dotnet test`
- `dotnet restore`
- `msbuild`
- `git status`
- `git diff`
- `git log`
- `git show`
- `git stash`

To customise, edit the `AllowedCommands` value in `appsettings.json` as a comma-separated list:

```json
{
  "Mcp": {
    "AllowedCommands": "dotnet build,dotnet test,dotnet restore,npm install,npm test"
  }
}
```

---

## Connecting an AI Agent

Once the service is running, you connect your AI agent to it by telling the agent where the service is and (if authentication is enabled) what key to use.

> **Important:** An API key is required for full and reliable tool operation. Without one, requests from some clients may be rejected or behave unexpectedly depending on network configuration. Always set `ApiKey` in `appsettings.json` before connecting any AI client.

### Service Endpoints

| Endpoint | Purpose |
|----------|---------|
| `http://localhost:55703/mcp/v1/` | Main MCP endpoint — the AI sends tool calls here |
| `http://localhost:55703/api/capabilities` | Machine-readable list of all tools with full schema |
| `http://localhost:55703/api/tools` | Simplified tool list |
| `http://localhost:55703/api/health` | Service health check |
| `http://localhost:55703/swagger` | Interactive API documentation (browser) |
| `http://localhost:55703/swagger/v1/swagger.json` | OpenAPI 3.0 specification (JSON) |

### How Tool Calls Work

The AI sends a JSON-RPC 2.0 request to `/mcp/v1/` specifying the tool name and its arguments. The service executes the tool and returns a JSON result. From the agent's perspective this is just a function call.

---

## Using with Ollama and Open WebUI

Stewardess works out of the box with a self-hosted Ollama + Open WebUI stack. This gives you a fully local AI coding assistant: no cloud, no data leaving your machine.

**What each part does:**

- **Ollama** — runs the large language model (LLM) locally on your machine
- **Open WebUI** — provides the chat interface and handles routing tool calls from the model to external tool servers
- **Stewardess** — receives those tool calls and executes them against your repository

Any Ollama model that supports tool calling will work. Models known to handle tool calls reliably include `llama3.1`, `qwen2.5-coder`, `mistral-nemo`, and `deepseek-coder-v2`.

### Before You Start

Make sure:
1. Ollama is installed and running (`ollama serve`)
2. You have pulled a tool-capable model, e.g. `ollama pull llama3.1`
3. Open WebUI is running (typically on `http://localhost:3000`)
4. Stewardess is running on `http://localhost:55703`
5. Stewardess has an `ApiKey` configured in `appsettings.json` — this is required for Open WebUI to authenticate correctly

### Recommended Stewardess Configuration for Open WebUI

```json
{
  "Mcp": {
    "RepositoryRoot": "C:\\Projects\\MyProject",
    "ApiKey": "choose-a-strong-secret-here"
  }
}
```

### Adding Stewardess as a Tool Server in Open WebUI

Open WebUI discovers available tools from the Stewardess OpenAPI specification and routes the model's tool calls to the service automatically. Follow these steps:

**Step 1 — Open the Admin Panel**

Log in to Open WebUI and click your profile picture in the top-right corner, then select **Admin Panel**.

**Step 2 — Go to Tools**

In the Admin Panel, click **Settings** in the left sidebar, then select **Tools**.

**Step 3 — Add a new Tool Server**

Click the **+** (Add) button to create a new tool server entry.

**Step 4 — Fill in the connection details**

| Field | Value |
|-------|-------|
| Name | `Stewardess MCP Service` (or any label you prefer) |
| URL | `http://localhost:55703/swagger/v1/swagger.json` |
| Authentication | Bearer Token |
| Token / API Key | The value you set in `Mcp.ApiKey` in `appsettings.json` |

> If Stewardess is running on a different machine, replace `localhost` with that machine's IP address or hostname.

**Step 5 — Save and verify**

Click **Save**. Open WebUI will fetch the OpenAPI spec from Stewardess and import all available tools. You should see them listed in the Tools section.

**Step 6 — Enable tools in a chat**

Open a new chat and select your Ollama model. Click the **Tools** icon (the spanner/wrench icon near the message input) and make sure Stewardess tools are toggled on. The model can now call any of the 74 Stewardess tools during the conversation.

### Talking to Your Code

Once connected, you can have conversations like:

- "What files are in the `src` folder?"
- "Find all usages of the `UserService` class"
- "Read the file `Program.cs` and explain what it does"
- "Build the project and show me any errors"
- "Build a code index of the repository and then find all classes that depend on `ILogger`"

The model will call the appropriate Stewardess tools automatically and include the results in its response.

### Troubleshooting Open WebUI Integration

| Problem | Likely Cause | Fix |
|---------|-------------|-----|
| Tools not appearing after saving | Wrong URL or Stewardess not running | Confirm `http://localhost:55703/api/health` returns a response |
| 401 Unauthorized errors | API key not set or wrong | Check `Mcp.ApiKey` in `appsettings.json` matches the token in Open WebUI |
| Model ignores tools | Model does not support tool calling | Switch to a tool-capable model such as `llama3.1` or `qwen2.5-coder` |
| Tool calls time out | Command execution taking too long | Increase `MaxCommandExecutionSeconds` in configuration |
| File access denied | Path is outside `RepositoryRoot` | Check the path the agent is using and ensure `RepositoryRoot` is set correctly |

---

## Safety Features

Stewardess is designed to be safe to run while you work:

- **Sandboxing** — All file operations are restricted to the `RepositoryRoot` folder. The agent cannot access files outside it.
- **Backups** — Before modifying any file, the service saves a backup copy. The backup location can be configured.
- **Rollback** — Any write operation returns a rollback token. The agent (or you) can use it to restore the previous version.
- **Read-only mode** — Set `ReadOnlyMode: true` to make the service entirely read-only. No writes, no commands.
- **Approval workflow** — Enable `RequireApprovalForDestructive` to require a preview step before any destructive action proceeds.
- **Audit log** — Every modifying operation is logged with a timestamp, the tool name, the arguments, and the outcome.
- **Command allow-list** — Command execution is limited to a specific list of approved command prefixes.
- **IP filtering** — Optionally restrict connections to specific IP addresses.

---

## Read-Only Mode Example

If you want an AI agent to analyse your code but never change anything, use this configuration:

```json
{
  "Mcp": {
    "RepositoryRoot": "C:\\Projects\\MyProject",
    "ReadOnlyMode": true,
    "ApiKey": "analysis-only-key"
  }
}
```

In this mode, the agent can use all navigation, search, reading, git, and code index tools but any attempt to write, edit, delete, or run commands will be rejected.

---

## Deploying to a Server

The service is a standard ASP.NET Core application and can be deployed anywhere .NET 8 runs.

**Publish a self-contained executable:**

```
cd StewardessMCPService.Core
dotnet publish -c Release -r win-x64 --self-contained
```

Replace `win-x64` with `linux-x64` for Linux.

**Run as a Windows service or Linux systemd unit:**

Point the process at `StewardessMCPService.Core.exe` (Windows) or the published binary (Linux) and supply the `RepositoryRoot` and `ApiKey` via environment variables rather than the config file, to avoid committing secrets.

**For remote access:**

If the service runs on a remote machine (not localhost), update the URLs in any agent configuration to use the remote host name or IP. Set `ApiKey` to a strong random value to secure the endpoint.

---

## Project Structure (Quick Reference)

| Folder | What It Contains |
|--------|-----------------|
| `StewardessMCPService.Core` | The runnable service: controllers, tool registry, startup, configuration |
| `StewardessMCPService.CodeIndexing` | The semantic code indexing engine (language-agnostic core) |
| `StewardessMCPService.Parsers.CSharp` | C# language adapter for the indexing engine |
| `StewardessMCPService.Tests` | Unit tests |
| `StewardessMCPService.IntegrationTests` | End-to-end integration tests |
| `StewardessMCPService.CodeIndexing.Tests` | Unit tests for the indexing engine |

---

## Quick-Start Checklist

1. Install .NET 8 SDK
2. Clone or download this repository
3. Edit `StewardessMCPService.Core\appsettings.json` and set `RepositoryRoot` to the folder you want the AI to work with
4. Set `ApiKey` to a strong secret value — this is required for Open WebUI and other clients to authenticate correctly
5. Run `dotnet run` from inside `StewardessMCPService.Core`
6. Confirm the service is running by visiting `http://localhost:55703/api/health`
7. If using Open WebUI, follow the steps in the **Using with Ollama and Open WebUI** section above to register Stewardess as a tool server
8. If using another MCP client, point it at `http://localhost:55703/mcp/v1/` with `Authorization: Bearer <your-api-key>`
9. If you want semantic code analysis, ask the agent to call `code_index.build` with the repository root path

---

## License

Copyright 2026 Alex Cherkasov

Licensed under the [Apache License, Version 2.0](LICENSE) (the "License"). You may not use this software except in compliance with the License.

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an **"AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND**, either express or implied. See the [LICENSE](LICENSE) file for the specific language governing permissions and limitations.
