using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StewardessMCPService.Models
{
    // Core entities from spec
    public class ApiUser
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
    }

    public class ApiRepository
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Private { get; set; }
        public ApiUser Owner { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    public class ApiBranch
    {
        public string Name { get; set; }
        public string CommitSha { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }

    public class ApiFile
    {
        public string Path { get; set; }
        public string Content { get; set; }
        public string Encoding { get; set; }
        public DateTimeOffset? LastModified { get; set; }
    }

    public class ApiDiff
    {
        public string FilePath { get; set; }
        public string DiffText { get; set; }
    }

    public class ApiCommit
    {
        public string Sha { get; set; }
        public string Message { get; set; }
        public ApiUser Author { get; set; }
        public List<ApiFile> Changes { get; set; } = new List<ApiFile>();
        public List<string> Parents { get; set; } = new List<string>();
    }

    public class ApiPullRequest
    {
        public string Id { get; set; }
        public string SourceBranch { get; set; }
        public string TargetBranch { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public ApiUser Author { get; set; }
        public string State { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? MergedAt { get; set; }
    }

    public class PaginatedList<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int Total { get; set; }
        public int Page { get; set; } = 1;
        public int Size { get; set; }
    }

    // Request types
    public class CreateRepositoryRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Private { get; set; }
    }

    public class CreateBranchRequest
    {
        public string Name { get; set; }
        public string SourceBranch { get; set; }
    }

    public class FileUpdateRequest
    {
        public string Path { get; set; }
        public string Content { get; set; }
        public string Encoding { get; set; } = "utf-8";
        public string Mode { get; set; } = "file";
    }

    public class FindFilesRequest
    {
        public string Path { get; set; }
        public string Pattern { get; set; }
        public bool Recursive { get; set; } = true;
        public bool IncludeHidden { get; set; }
    }

    public class DiffRequest
    {
        public string BaseSha { get; set; }
        public string TargetSha { get; set; }
    }

    public class CommitRequest
    {
        public string Message { get; set; }
        public ApiUser Author { get; set; }
        public List<FileUpdateRequest> Changes { get; set; } = new List<FileUpdateRequest>();
        public List<string> Parents { get; set; } = new List<string>();
    }

    public class MergeRequest
    {
        public string SourceBranch { get; set; }
        public string TargetBranch { get; set; }
        public string Strategy { get; set; } = "recursive";
    }

    public class PullRequestRequest
    {
        public string SourceBranch { get; set; }
        public string TargetBranch { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
    }
}
