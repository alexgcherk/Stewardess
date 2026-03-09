// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Enforces the security policy for every service operation.
    /// Implementations are synchronous because all checks are in-process.
    /// </summary>
    public interface ISecurityService
    {
        // ── Path sandbox ─────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a caller-supplied relative path to an absolute path and validates
        /// that it is inside the configured RepositoryRoot.
        /// Returns a <see cref="SecurityCheckResult"/> rather than throwing so that
        /// callers can return structured error responses.
        /// </summary>
        SecurityCheckResult ValidatePath(string relativePath, out string absolutePath);

        /// <summary>
        /// Validates a path AND checks that neither the file extension nor any
        /// ancestor directory is blocked.
        /// </summary>
        SecurityCheckResult ValidateReadPath(string relativePath, out string absolutePath);

        /// <summary>
        /// Validates a path for write access: sandbox + extension + read-only mode check.
        /// </summary>
        SecurityCheckResult ValidateWritePath(string relativePath, out string absolutePath);

        // ── Authentication ───────────────────────────────────────────────────────

        /// <summary>Validates the supplied API key against the configured value.</summary>
        bool ValidateApiKey(string suppliedKey);

        /// <summary>Returns true when the given IP address is allowed by the IP allowlist.</summary>
        bool IsIpAllowed(string clientIp);

        // ── Operation guards ─────────────────────────────────────────────────────

        /// <summary>Returns true when write/edit operations are permitted (i.e. not in read-only mode).</summary>
        bool CanWrite();

        /// <summary>Validates a destructive-operation approval token.</summary>
        bool ValidateApprovalToken(string? token);

        /// <summary>Generates a one-time approval token for a pending destructive operation.</summary>
        string GenerateApprovalToken(string operationDescription);
    }

    /// <summary>Result of a security validation check.</summary>
    public sealed class SecurityCheckResult
    {
        /// <summary>True when the operation is permitted.</summary>
        public bool IsAllowed { get; set; }
        /// <summary>Machine-readable error code when the check failed; null on success.</summary>
        public string? ErrorCode { get; set; }
        /// <summary>Human-readable error message when the check failed; null on success.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Creates a passing result.</summary>
        public static SecurityCheckResult Allow() =>
            new SecurityCheckResult { IsAllowed = true };

        /// <summary>Creates a failing result with the given error code and message.</summary>
        public static SecurityCheckResult Deny(string code, string message) =>
            new SecurityCheckResult { IsAllowed = false, ErrorCode = code, ErrorMessage = message };
    }
}
