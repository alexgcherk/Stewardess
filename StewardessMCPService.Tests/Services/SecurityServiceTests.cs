// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using StewardessMCPService.Tests.Helpers;
using Xunit;

namespace StewardessMCPService.Tests.Services;

/// <summary>
///     Unit tests for <see cref="SecurityService" />.
///     Covers API key validation, IP allowlist, and path authorization.
/// </summary>
public sealed class SecurityServiceTests : IDisposable
{
    private readonly TempRepository _repo;

    public SecurityServiceTests()
    {
        _repo = new TempRepository();
    }

    public void Dispose()
    {
        _repo.Dispose();
    }

    // ── API key validation ───────────────────────────────────────────────────

    [Fact]
    public void ValidateApiKey_NoKeyRequired_AlwaysTrue()
    {
        var svc = BuildService("");
        Assert.True(svc.ValidateApiKey("anything"));
        Assert.True(svc.ValidateApiKey(""));
        Assert.True(svc.ValidateApiKey(null!));
    }

    [Fact]
    public void ValidateApiKey_CorrectKey_ReturnsTrue()
    {
        var svc = BuildService("secret-key-123");
        Assert.True(svc.ValidateApiKey("secret-key-123"));
    }

    [Fact]
    public void ValidateApiKey_WrongKey_ReturnsFalse()
    {
        var svc = BuildService("secret-key-123");
        Assert.False(svc.ValidateApiKey("wrong-key"));
        Assert.False(svc.ValidateApiKey(""));
        Assert.False(svc.ValidateApiKey(null!));
    }

    [Fact]
    public void ValidateApiKey_CaseSensitive()
    {
        var svc = BuildService("MyKey");
        Assert.False(svc.ValidateApiKey("mykey"));
        Assert.False(svc.ValidateApiKey("MYKEY"));
    }

    // ── IP allowlist ─────────────────────────────────────────────────────────

    [Fact]
    public void IsIpAllowed_EmptyAllowlist_AllowsAll()
    {
        var svc = BuildService(allowedIps: new string[0]);
        Assert.True(svc.IsIpAllowed("192.168.1.100"));
        Assert.True(svc.IsIpAllowed("1.2.3.4"));
    }

    [Fact]
    public void IsIpAllowed_AllowedIp_ReturnsTrue()
    {
        var svc = BuildService(allowedIps: new[] { "127.0.0.1", "::1" });
        Assert.True(svc.IsIpAllowed("127.0.0.1"));
        Assert.True(svc.IsIpAllowed("::1"));
    }

    [Fact]
    public void IsIpAllowed_DisallowedIp_ReturnsFalse()
    {
        var svc = BuildService(allowedIps: new[] { "127.0.0.1" });
        Assert.False(svc.IsIpAllowed("192.168.1.50"));
    }

    // ── Path authorization ───────────────────────────────────────────────────

    [Fact]
    public void ValidateReadPath_ValidFile_Allowed()
    {
        _repo.CreateFile(@"src\Class1.cs", "code");
        var svc = BuildService();
        var result = svc.ValidateReadPath(@"src\Class1.cs", out _);
        Assert.True(result.IsAllowed, result.ErrorMessage);
    }

    [Fact]
    public void ValidateWritePath_ReadOnlyMode_Denied()
    {
        var svc = BuildService(readOnly: true);
        var result = svc.ValidateWritePath(@"src\Class1.cs", out _);
        Assert.False(result.IsAllowed);
        Assert.Equal(ErrorCodes.ReadOnlyMode, result.ErrorCode);
    }

    [Fact]
    public void ValidateWritePath_NormalMode_Allowed()
    {
        var svc = BuildService(readOnly: false);
        var result = svc.ValidateWritePath(@"src\Class1.cs", out _);
        Assert.True(result.IsAllowed, result.ErrorMessage);
    }

    // ── Approval tokens ──────────────────────────────────────────────────────

    [Fact]
    public void ApprovalTokens_IssueAndValidate_Success()
    {
        // When RequireApprovalForDestructive = false, ValidateApprovalToken always passes.
        var svc = BuildService();
        var token = svc.GenerateApprovalToken("delete_file src/Class1.cs");
        Assert.False(string.IsNullOrEmpty(token));
        // RequireApprovalForDestructive is false in test settings, so any token passes.
        Assert.True(svc.ValidateApprovalToken(token));
    }

    [Fact]
    public void ApprovalToken_WhenNotRequired_NullTokenPasses()
    {
        var svc = BuildService();
        // When approval is not required, even null passes.
        Assert.True(svc.ValidateApprovalToken(null!));
    }

    [Fact]
    public void CanWrite_ReadOnlyMode_ReturnsFalse()
    {
        var svc = BuildService(readOnly: true);
        Assert.False(svc.CanWrite());
    }

    [Fact]
    public void CanWrite_NormalMode_ReturnsTrue()
    {
        var svc = BuildService(readOnly: false);
        Assert.True(svc.CanWrite());
    }

    // ── Security regression: constant-time API key comparison (S09) ───────────

    [Fact]
    public void ValidateApiKey_NullInput_ReturnsFalse()
    {
        // Null must never throw and must return false when a key is configured.
        var svc = BuildService("real-key");
        Assert.False(svc.ValidateApiKey(null!));
    }

    [Fact]
    public void ValidateApiKey_SamePrefixShorterKey_ReturnsFalse()
    {
        // A key that shares a prefix with the real key must be rejected.
        // (Tests that length check works correctly.)
        var svc = BuildService("real-key-long");
        Assert.False(svc.ValidateApiKey("real-key"));
    }

    [Fact]
    public void ValidateApiKey_SamePrefixLongerKey_ReturnsFalse()
    {
        // A longer key with the correct prefix must be rejected.
        var svc = BuildService("real-key");
        Assert.False(svc.ValidateApiKey("real-key-extra"));
    }

    [Fact]
    public void ValidateApiKey_OneByteOff_ReturnsFalse()
    {
        // Only the last character differs — must be rejected even though the
        // prefix matches completely (tests that the full string is compared).
        var svc = BuildService("abcdefgh");
        Assert.False(svc.ValidateApiKey("abcdefgX"));
    }

    [Fact]
    public void ValidateApiKey_EmptyKey_ReturnsFalse()
    {
        // Empty string must not match a non-empty configured key.
        var svc = BuildService("some-key");
        Assert.False(svc.ValidateApiKey(""));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private SecurityService BuildService(
        string apiKey = "",
        bool readOnly = false,
        string[]? allowedIps = null)
    {
        var settings = McpServiceSettings.CreateForTesting(
            _repo.Root,
            readOnly,
            apiKey: apiKey,
            allowedIps: allowedIps!,
            blockedFolders: new[] { ".git", "bin", "obj" },
            blockedExtensions: new[] { ".exe", ".dll" });

        var pathValidator = new PathValidator(settings);
        return new SecurityService(settings, pathValidator);
    }
}