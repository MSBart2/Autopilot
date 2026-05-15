using System.Text.Json;

namespace Cyberpilot.GitHub;

/// <summary>
/// Summarizes one GitHub issue comment for cleanup and replay workflows.
/// </summary>
/// <param name="Id">The GitHub REST comment identifier.</param>
/// <param name="Body">The comment body.</param>
/// <param name="AuthorLogin">The comment author's login.</param>
public sealed record GitHubIssueComment(long Id, string Body, string AuthorLogin);

/// <summary>
/// Provides issue, label, and comment operations needed by Cyberpilot.
/// </summary>
public interface IGitHubIssueClient
{
    /// <summary>Adds a label to an issue.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="label">The label to add.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default);
    /// <summary>Creates a comment on an issue.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="body">The comment body.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default);
    /// <summary>Lists comments on an issue.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The issue comments.</returns>
    Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default);
    /// <summary>Deletes one issue comment.</summary>
    /// <param name="commentId">The GitHub REST comment identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default);
    /// <summary>Gets one issue by number.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The issue summary, or null when not found.</returns>
    Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default);
    /// <summary>Gets issue labels.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The issue label names.</returns>
    Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default);
    /// <summary>Gets the issue state.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The issue state.</returns>
    Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default);
    /// <summary>Gets repository labels.</summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The repository label names.</returns>
    Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default);
    /// <summary>Lists open issues.</summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The open issue summaries.</returns>
    Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default);
    /// <summary>Lists open pull requests.</summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The open pull request summaries with <see cref="GitHubIssueSummary.IsPullRequest"/> set to true.</returns>
    Task<IReadOnlyList<GitHubIssueSummary>> ListOpenPullRequestsAsync(CancellationToken cancellationToken = default);
    /// <summary>Finds an open pull request linked to the given issue number.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The PR info, or null if no open PR is linked.</returns>
    Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default);
    /// <summary>Removes a label from an issue.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="label">The label to remove.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default);
    /// <summary>Closes an issue.</summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default);
    /// <summary>Creates or updates a repository label.</summary>
    /// <param name="label">The label name.</param>
    /// <param name="color">The hex color without a leading hash.</param>
    /// <param name="description">The label description.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default);
}

internal sealed class GitHubIssueClient(IGitHubCli cli) : IGitHubIssueClient
{
    public async Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default)
    {
        await cli.RunAsync(["issue", "edit", issueNumber.ToString(), "--add-label", label], cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        var result = await cli.RunAsync(["issue", "view", issueNumber.ToString(), "--json", "labels", "--jq", ".labels[].name"], cancellationToken: cancellationToken);
        return LineSplitter.Split(result);
    }

    public async Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default)
    {
        await cli.RunAsync(["issue", "comment", issueNumber.ToString(), "--body", body], cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        var result = await cli.RunAsync(["api", $"repos/:owner/:repo/issues/{issueNumber}/comments", "--paginate"], cancellationToken: cancellationToken);
        return GitHubIssueCommentJson.ParseMany(result);
    }

    public async Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default)
    {
        await cli.RunAsync(["api", "-X", "DELETE", $"repos/:owner/:repo/issues/comments/{commentId}"], allowFailure: true, cancellationToken);
    }

    public async Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        var title = (await cli.RunAsync(["issue", "view", issueNumber.ToString(), "--json", "title", "--jq", ".title"], cancellationToken: cancellationToken)).Trim();
        var body = (await cli.RunAsync(["issue", "view", issueNumber.ToString(), "--json", "body", "--jq", ".body"], cancellationToken: cancellationToken)).Trim();
        var state = await GetIssueStateAsync(issueNumber, cancellationToken);
        return new GitHubIssueSummary(issueNumber, title, string.Empty, [], DateTimeOffset.MinValue, state, false, body);
    }

    public async Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        return (await cli.RunAsync(["issue", "view", issueNumber.ToString(), "--json", "state", "--jq", ".state"], cancellationToken: cancellationToken)).Trim();
    }

    public async Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default)
    {
        var result = await cli.RunAsync(["label", "list", "--limit", "200", "--json", "name", "--jq", ".[].name"], cancellationToken: cancellationToken);
        return LineSplitter.Split(result).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default)
    {
        var result = await cli.RunAsync(["issue", "list", "--state", "open", "--limit", "50", "--json", "number,title,url,labels,updatedAt"], cancellationToken: cancellationToken);
        return GitHubIssueSummaryJson.ParseMany(result);
    }

    public async Task<IReadOnlyList<GitHubIssueSummary>> ListOpenPullRequestsAsync(CancellationToken cancellationToken = default)
    {
        var result = await cli.RunAsync(["pr", "list", "--state", "open", "--limit", "50", "--json", "number,title,url,headRefName,labels,updatedAt"], allowFailure: true, cancellationToken);
        if (string.IsNullOrWhiteSpace(result)) return [];
        using var doc = JsonDocument.Parse(result);
        return doc.RootElement.EnumerateArray()
            .Select(GitHubIssueSummaryJson.ParsePullRequest)
            .ToArray();
    }

    public async Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default)
    {
        await cli.RunAsync(["issue", "edit", issueNumber.ToString(), "--remove-label", label], allowFailure: true, cancellationToken);
    }

    public async Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        await cli.RunAsync(["issue", "close", issueNumber.ToString()], cancellationToken: cancellationToken);
    }

    public async Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        // Search for open PRs whose title or body mentions the issue number
        var result = await cli.RunAsync(
            ["pr", "list", "--state", "open", "--search", $"issue {issueNumber}", "--json", "number,url,headRefName,state", "--limit", "10"],
            allowFailure: true, cancellationToken);
        if (string.IsNullOrWhiteSpace(result)) return null;

        using var doc = JsonDocument.Parse(result);
        foreach (var pr in doc.RootElement.EnumerateArray())
        {
            var headRef = pr.TryGetProperty("headRefName", out var h) ? h.GetString() ?? "" : "";
            if (GitHubPullRequestMatcher.IsIssueBranch(headRef, issueNumber))
            {
                var number = pr.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
                var url = pr.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var state = pr.TryGetProperty("state", out var s) ? s.GetString() ?? "OPEN" : "OPEN";
                return new GitHubPullRequestInfo(number, url, headRef, state);
            }
        }

        return null;
    }

    public async Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default)
    {
        await cli.RunAsync(["label", "create", label, "--color", color, "--description", description, "--force"], cancellationToken: cancellationToken);
    }
}
