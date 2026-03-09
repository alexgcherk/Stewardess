// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Heuristic project and solution detection without compiling or invoking MSBuild.
    /// Results are best-effort and based on file system inspection and text parsing.
    /// </summary>
    public interface IProjectDetectionService
    {
        /// <summary>Finds all .sln files under the repository root.</summary>
        Task<List<string>> FindSolutionFilesAsync(CancellationToken ct = default);

        /// <summary>Finds all project files (.csproj, .vbproj, .fsproj, etc.).</summary>
        Task<List<ProjectInfo>> FindProjectsAsync(CancellationToken ct = default);

        /// <summary>Parses a solution file and returns the projects it references.</summary>
        Task<List<ProjectInfo>> ParseSolutionAsync(string solutionRelativePath, CancellationToken ct = default);

        /// <summary>Returns projects whose name or output type suggests they contain tests.</summary>
        Task<List<ProjectInfo>> FindTestProjectsAsync(CancellationToken ct = default);

        /// <summary>Locates common configuration files (app.config, web.config, appsettings.json, etc.).</summary>
        Task<ConfigFilesResponse> FindConfigFilesAsync(CancellationToken ct = default);

        /// <summary>Returns a combined solution/project overview for the whole repository.</summary>
        Task<SolutionInfoResponse> GetSolutionInfoAsync(CancellationToken ct = default);
    }
}
