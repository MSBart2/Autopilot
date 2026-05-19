using Cyberpilot.GitHub;

namespace Cyberpilot.Web.Controllers;

/// <summary>
/// GitHub issue cleanup and label management helpers.
/// </summary>
internal static class GitHubIssueHelper
{
    /// <summary>
    /// Resets issue labels to clean SDK state by removing all sdk/* labels and ensuring sdk label exists.
    /// </summary>
    /// <param name="issueClient">GitHub issue client.</param>
    /// <param name="issueNumber">Issue number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task ResetIssueLabelsAsync(IGitHubIssueClient issueClient, int issueNumber, CancellationToken cancellationToken)
    {
        var labels = await issueClient.GetIssueLabelsAsync(issueNumber, cancellationToken);
        foreach (var label in labels.Where(label => label.StartsWith("sdk/", StringComparison.OrdinalIgnoreCase)))
        {
            await issueClient.RemoveIssueLabelAsync(issueNumber, label, cancellationToken);
        }

        if (!labels.Contains("sdk", StringComparer.OrdinalIgnoreCase))
        {
            await issueClient.AddIssueLabelAsync(issueNumber, "sdk", cancellationToken);
        }
    }

    /// <summary>
    /// Deletes all Cyberpilot agent comments from an issue.
    /// </summary>
    /// <param name="issueClient">GitHub issue client.</param>
    /// <param name="issueNumber">Issue number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of comments deleted.</returns>
    public static async Task<int> DeleteAgentCommentsAsync(IGitHubIssueClient issueClient, int issueNumber, CancellationToken cancellationToken)
    {
        var comments = await issueClient.ListIssueCommentsAsync(issueNumber, cancellationToken);
        var deleted = 0;
        foreach (var comment in comments.Where(comment => IsAgentComment(comment.Body)))
        {
            await issueClient.DeleteIssueCommentAsync(comment.Id, cancellationToken);
            deleted++;
        }

        return deleted;
    }

    /// <summary>
    /// Checks whether a comment body contains Cyberpilot agent markers.
    /// </summary>
    /// <param name="body">Comment body text.</param>
    /// <returns>True if the comment appears to be from a Cyberpilot agent.</returns>
    public static bool IsAgentComment(string body)
        => CyberpilotIssueCommentClassifier.IsAgentComment(body);
}
