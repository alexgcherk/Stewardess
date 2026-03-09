// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.Models;

// Core entities from spec
public class ApiUser
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string? Email { get; set; }
}

public class ApiRepository
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool Private { get; set; }
    public ApiUser Owner { get; set; } = null!;
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class ApiBranch
{
    public string Name { get; set; } = null!;
    public string CommitSha { get; set; } = null!;
    public DateTimeOffset? CreatedAt { get; set; }
}

public class ApiFile
{
    public string Path { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? Encoding { get; set; }
    public DateTimeOffset? LastModified { get; set; }
}

public class ApiDiff
{
    public string FilePath { get; set; } = null!;
    public string DiffText { get; set; } = null!;
}

public class ApiCommit
{
    public string Sha { get; set; } = null!;
    public string Message { get; set; } = null!;
    public ApiUser Author { get; set; } = null!;
    public List<ApiFile> Changes { get; set; } = new();
    public List<string> Parents { get; set; } = new();
}

public class ApiPullRequest
{
    public string Id { get; set; } = null!;
    public string SourceBranch { get; set; } = null!;
    public string TargetBranch { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Body { get; set; }
    public ApiUser Author { get; set; } = null!;
    public string State { get; set; } = null!;
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
}

public class PaginatedList<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int Size { get; set; }
}

// Request types
public class CreateRepositoryRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool Private { get; set; }
}

public class CreateBranchRequest
{
    public string Name { get; set; } = null!;
    public string SourceBranch { get; set; } = null!;
}

public class FileUpdateRequest
{
    public string Path { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string Encoding { get; set; } = "utf-8";
    public string Mode { get; set; } = "file";
}

public class FindFilesRequest
{
    public string Path { get; set; } = null!;
    public string? Pattern { get; set; }
    public bool Recursive { get; set; } = true;
    public bool IncludeHidden { get; set; }
}

public class DiffRequest
{
    public string BaseSha { get; set; } = null!;
    public string TargetSha { get; set; } = null!;
}

public class CommitRequest
{
    public string Message { get; set; } = null!;
    public ApiUser Author { get; set; } = null!;
    public List<FileUpdateRequest> Changes { get; set; } = new();
    public List<string> Parents { get; set; } = new();
}

public class MergeRequest
{
    public string SourceBranch { get; set; } = null!;
    public string TargetBranch { get; set; } = null!;
    public string Strategy { get; set; } = "recursive";
}

public class PullRequestRequest
{
    public string SourceBranch { get; set; } = null!;
    public string TargetBranch { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Body { get; set; }
}