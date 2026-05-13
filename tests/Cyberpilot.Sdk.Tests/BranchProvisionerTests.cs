using Cyberpilot.Git;

namespace Cyberpilot.Sdk.Tests;

public sealed class BranchProvisionerTests
{
    [Fact]
    public void CreateBranchName_BasicTitle()
    {
        var name = BranchProvisioner.CreateBranchName(42, "Add login");

        Assert.Equal("sdk/issue-42-add-login", name);
    }

    [Fact]
    public void CreateBranchName_SpecialCharacters()
    {
        var name = BranchProvisioner.CreateBranchName(5, "Fix: bug #123 (urgent!)");

        Assert.StartsWith("sdk/issue-5-", name);
        Assert.DoesNotContain(":", name);
        Assert.DoesNotContain("#", name);
        Assert.DoesNotContain("(", name);
        Assert.DoesNotContain("!", name);
    }

    [Fact]
    public void CreateBranchName_EmptyTitle()
    {
        var name = BranchProvisioner.CreateBranchName(1, "");

        Assert.Equal("sdk/issue-1-work", name);
    }

    [Fact]
    public void CreateBranchName_LongTitle()
    {
        var longTitle = new string('a', 120);
        var name = BranchProvisioner.CreateBranchName(7, longTitle);

        // "sdk/issue-7-" prefix + slug (max 48 chars)
        var slug = name["sdk/issue-7-".Length..];
        Assert.True(slug.Length <= 48);
    }

    [Fact]
    public void CreateBranchName_UppercaseNormalized()
    {
        var name = BranchProvisioner.CreateBranchName(10, "FIX LOGIN BUG");

        Assert.Equal("sdk/issue-10-fix-login-bug", name);
    }

    [Fact]
    public void CreateBranchName_LeadingTrailingSpecialChars()
    {
        var name = BranchProvisioner.CreateBranchName(3, "!!fix this!!");

        Assert.Equal("sdk/issue-3-fix-this", name);
    }
}
