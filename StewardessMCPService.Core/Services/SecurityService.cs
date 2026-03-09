// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services;

/// <summary>
///     Enforces all security policies: path sandbox, authentication,
///     IP allowlist, read-only mode, and approval tokens.
/// </summary>
public sealed class SecurityService : ISecurityService
{
    private static readonly McpLogger _log = McpLogger.For<SecurityService>();

    // One-time approval tokens: token → expiry
    private readonly ConcurrentDictionary<string, DateTimeOffset> _approvalTokens = new(StringComparer.Ordinal);

    private readonly PathValidator _pathValidator;
    private readonly McpServiceSettings _settings;

    /// <summary>Initialises a new instance of <see cref="SecurityService" />.</summary>
    public SecurityService(McpServiceSettings settings, PathValidator pathValidator)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    // ── Path validation ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public SecurityCheckResult ValidatePath(string relativePath, out string absolutePath)
    {
        var result = _pathValidator.Validate(relativePath, out absolutePath);
        return result.IsValid
            ? SecurityCheckResult.Allow()
            : SecurityCheckResult.Deny(result.ErrorCode, result.ErrorMessage);
    }

    /// <inheritdoc />
    public SecurityCheckResult ValidateReadPath(string relativePath, out string absolutePath)
    {
        var result = _pathValidator.ValidateRead(relativePath, out absolutePath);
        return result.IsValid
            ? SecurityCheckResult.Allow()
            : SecurityCheckResult.Deny(result.ErrorCode, result.ErrorMessage);
    }

    /// <inheritdoc />
    public SecurityCheckResult ValidateWritePath(string relativePath, out string absolutePath)
    {
        if (_settings.ReadOnlyMode)
        {
            absolutePath = null!;
            return SecurityCheckResult.Deny(
                ErrorCodes.ReadOnlyMode,
                "The service is running in read-only mode. Write operations are not permitted.");
        }

        var result = _pathValidator.ValidateWrite(relativePath, out absolutePath);
        return result.IsValid
            ? SecurityCheckResult.Allow()
            : SecurityCheckResult.Deny(result.ErrorCode, result.ErrorMessage);
    }

    // ── Authentication ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public bool ValidateApiKey(string suppliedKey)
    {
        if (!_settings.RequireApiKey) return true;
        // Constant-time comparison — prevents timing side-channel enumeration of the key.
        return ConstantTimeEquals(_settings.ApiKey, suppliedKey);
    }

    /// <inheritdoc />
    public bool IsIpAllowed(string clientIp)
    {
        if (_settings.AllowedIPs.Count == 0) return true;
        foreach (var allowed in _settings.AllowedIPs)
            if (string.Equals(allowed, clientIp, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <inheritdoc />
    public bool CanWrite()
    {
        return !_settings.ReadOnlyMode;
    }

    // ── Approval tokens ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public string GenerateApprovalToken(string operationDescription)
    {
        // Purge expired tokens opportunistically.
        PurgeExpiredTokens();

        var token = Guid.NewGuid().ToString("N");
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        _approvalTokens[token] = expiry;

        _log.Info($"Approval token generated for: {operationDescription} (expires {expiry:u})");
        return token;
    }

    /// <inheritdoc />
    public bool ValidateApprovalToken(string? token)
    {
        if (!_settings.RequireApprovalForDestructive) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;

        if (_approvalTokens.TryRemove(token, out var expiry))
        {
            if (DateTimeOffset.UtcNow <= expiry)
                return true;

            _log.Warn($"Approval token expired at {expiry:u}");
            return false;
        }

        _log.Warn("Unknown or already-used approval token.");
        return false;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a == null || b == null) return false;
        var diff = a.Length ^ b.Length;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private void PurgeExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _approvalTokens)
            if (kv.Value < now)
                _approvalTokens.TryRemove(kv.Key, out _);
    }
}