using Cyberpilot.GitHub;

namespace Cyberpilot.Web.UnitTests.Services;

public sealed class GitHubRepositoryParserTests
{
    [Theory]
    [InlineData("owner/repo", "owner/repo")]
    [InlineData(" https://github.com/owner/repo ", "owner/repo")]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "owner/repo")]
    [InlineData("https://github.com/owner/repo/", "owner/repo")]
    public void TryNormalize_WithSupportedFormats_ReturnsOwnerName(string input, string expected)
    {
        var result = GitHubRepositoryParser.TryNormalize(input, out var repository);

        Assert.True(result);
        Assert.Equal(expected, repository);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("owner/re po")]
    public void TryNormalize_WithUnsupportedFormats_ReturnsFalse(string? input)
    {
        var result = GitHubRepositoryParser.TryNormalize(input, out var repository);

        Assert.False(result);
        Assert.Equal(string.Empty, repository);
    }
}