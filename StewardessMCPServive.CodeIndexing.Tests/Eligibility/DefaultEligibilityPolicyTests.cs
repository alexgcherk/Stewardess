using StewardessMCPServive.CodeIndexing.Eligibility;
using StewardessMCPServive.CodeIndexing.Model.Structural;
using Xunit;

namespace StewardessMCPServive.CodeIndexing.Tests.Eligibility;

public class DefaultEligibilityPolicyTests
{
    private readonly DefaultEligibilityPolicy _policy = new();

    [Fact]
    public void Evaluate_NormalSourceFile_IsEligible()
    {
        var result = _policy.Evaluate("src/MyClass.cs", 1024, isBinary: false);
        Assert.Equal(EligibilityStatus.Eligible, result.Status);
        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_BinaryFile_IsExcluded()
    {
        var result = _policy.Evaluate("image.png", 1024, isBinary: true);
        Assert.Equal(EligibilityStatus.Binary, result.Status);
        Assert.False(result.IsEligible);
    }

    [Theory]
    [InlineData("file.exe")]
    [InlineData("file.dll")]
    [InlineData("file.bin")]
    [InlineData("file.png")]
    [InlineData("file.jpg")]
    [InlineData("file.zip")]
    [InlineData("file.pdf")]
    public void Evaluate_BinaryExtension_IsNotEligible(string filename)
    {
        var result = _policy.Evaluate(filename, 100, isBinary: false);
        // Policy returns Excluded for known-binary extensions
        Assert.False(result.IsEligible);
    }

    [Theory]
    [InlineData("bin/Debug/MyService.cs")]
    [InlineData("obj/Release/Temp.cs")]
    [InlineData("node_modules/lodash/index.js")]
    [InlineData(".git/config")]
    public void Evaluate_IgnoredFolder_IsIgnored(string path)
    {
        var result = _policy.Evaluate(path, 100, isBinary: false);
        Assert.Equal(EligibilityStatus.Ignored, result.Status);
    }

    [Fact]
    public void Evaluate_FileTooLarge_IsTooLarge()
    {
        var bigSize = _policy.MaxFileSizeBytes + 1;
        var result = _policy.Evaluate("huge.cs", bigSize, isBinary: false);
        Assert.Equal(EligibilityStatus.TooLarge, result.Status);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".DS_Store")]
    [InlineData("secret.tmp")]
    [InlineData("backup.bak")]
    public void Evaluate_HiddenOrSystemFile_IsNotEligible(string filename)
    {
        var result = _policy.Evaluate(filename, 100, isBinary: false);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void MaxFileSizeBytes_IsPositive()
    {
        Assert.True(_policy.MaxFileSizeBytes > 0);
    }

    [Fact]
    public void Evaluate_GeneratedFile_IsGenerated()
    {
        var result = _policy.Evaluate("MyService.Designer.cs", 500, isBinary: false);
        Assert.Equal(EligibilityStatus.Generated, result.Status);
    }
}
