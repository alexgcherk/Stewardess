// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPService.Configuration;
using StewardessMCPService.Models;
using StewardessMCPService.Services;

namespace StewardessMCPService.Controllers
{
    /// <summary>
    /// Controlled command execution: build, test, and custom allow-listed commands.
    /// All requests are validated against the AllowedCommands whitelist.
    /// </summary>
    [Route("api/command")]
    public sealed class CommandController : BaseController
    {
        private ICommandService      CommandService => GetService<ICommandService>();
        private McpServiceSettings   Settings       => GetService<McpServiceSettings>();

        // ── GET /api/command/allowed ────────────────────────────────────────────

        /// <summary>Lists the commands that are currently permitted by configuration.</summary>
        [HttpGet, Route("allowed")]
        public IActionResult GetAllowedCommands()
        {
            var commands = Settings.AllowedCommands;
            return Ok(new
            {
                AllowedCommands = commands,
                Count = commands.Count,
                ReadOnlyMode = Settings.ReadOnlyMode
            });
        }

        // ── POST /api/command/build ─────────────────────────────────────────────

        /// <summary>
        /// Runs the configured build command (e.g. <c>dotnet build</c> or <c>msbuild</c>).
        /// </summary>
        [HttpPost, Route("build")]
        public async Task<IActionResult> RunBuild(
            [FromBody] RunBuildRequest request, CancellationToken ct)
        {
            if (Settings.ReadOnlyMode)
                return Fail(
                    System.Net.HttpStatusCode.Forbidden,
                    ErrorCodes.ReadOnlyMode,
                    "Service is in read-only mode.");

            try
            {
                var result = await CommandService
                    .RunBuildAsync(request ?? new RunBuildRequest(), ct)
                    .ConfigureAwait(false);

                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbidden(ErrorCodes.CommandNotAllowed, ex.Message);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        // ── POST /api/command/test ──────────────────────────────────────────────

        /// <summary>
        /// Runs the configured test command (e.g. <c>dotnet test</c>).
        /// </summary>
        [HttpPost, Route("test")]
        public async Task<IActionResult> RunTests(
            [FromBody] RunTestsRequest request, CancellationToken ct)
        {
            if (Settings.ReadOnlyMode)
                return Fail(
                    System.Net.HttpStatusCode.Forbidden,
                    ErrorCodes.ReadOnlyMode,
                    "Service is in read-only mode.");

            try
            {
                var result = await CommandService
                    .RunTestsAsync(request ?? new RunTestsRequest(), ct)
                    .ConfigureAwait(false);

                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbidden(ErrorCodes.CommandNotAllowed, ex.Message);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        // ── POST /api/command/run ───────────────────────────────────────────────

        /// <summary>
        /// Runs a custom command that must appear in the AllowedCommands whitelist.
        /// </summary>
        [HttpPost, Route("run")]
        public async Task<IActionResult> RunCustomCommand(
            [FromBody] RunCustomCommandRequest request, CancellationToken ct)
        {
            if (Settings.ReadOnlyMode)
                return Fail(
                    System.Net.HttpStatusCode.Forbidden,
                    ErrorCodes.ReadOnlyMode,
                    "Service is in read-only mode.");

            if (request == null || string.IsNullOrWhiteSpace(request.Command))
                return BadRequest(ErrorCodes.InvalidRequest, "command is required.");

            if (!CommandService.IsCommandAllowed(request.Command))
                return Forbidden(ErrorCodes.CommandNotAllowed,
                    $"Command not in AllowedCommands list: {request.Command}");

            try
            {
                var result = await CommandService
                    .RunCustomCommandAsync(request, ct)
                    .ConfigureAwait(false);

                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbidden(ErrorCodes.CommandNotAllowed, ex.Message);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }
    }
}
