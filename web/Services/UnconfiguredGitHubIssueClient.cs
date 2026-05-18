using Cyberpilot.GitHub;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Provides clear dashboard errors when GitHub credentials are not configured.
/// </summary>
public sealed class UnconfiguredGitHubIssueClient(string message) : IGitHubIssueClient
{
    /// <inheritdoc />
    public Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubIssueSummary>> ListOpenPullRequestsAsync(CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task ClosePullRequestAsync(int pullRequestNumber, CancellationToken cancellationToken = default) => throw CreateException();

    /// <inheritdoc />
    public Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => throw CreateException();

    private InvalidOperationException CreateException() => new(message);
}