using Cyberpilot.Web.Services;

namespace Cyberpilot.Web.UnitTests.Services;

public class UnconfiguredGitHubIssueClientTests
{
    private const string ErrorMessage = "GitHub token not configured";
    private readonly UnconfiguredGitHubIssueClient _client = new(ErrorMessage);

    [Fact]
    public void AddIssueLabelAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.AddIssueLabelAsync(1, "bug").GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void CommentAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.CommentAsync(1, "test").GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void GetIssueAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.GetIssueAsync(1).GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void GetIssueLabelsAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.GetIssueLabelsAsync(1).GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void GetIssueStateAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.GetIssueStateAsync(1).GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void GetRepositoryLabelsAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.GetRepositoryLabelsAsync().GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void ListOpenIssuesAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.ListOpenIssuesAsync().GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void RemoveIssueLabelAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.RemoveIssueLabelAsync(1, "x").GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void CreateOrUpdateLabelAsync_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _client.CreateOrUpdateLabelAsync("x", "c", "d").GetAwaiter().GetResult());
        Assert.Equal(ErrorMessage, ex.Message);
    }
}
