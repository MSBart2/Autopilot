namespace Cyberpilot.GitHub;

/// <summary>
/// Minimal pull request info returned by issue-linked PR searches.
/// </summary>
/// <param name="Number">The PR number.</param>
/// <param name="Url">The PR URL.</param>
/// <param name="HeadBranch">The head branch name.</param>
/// <param name="BaseBranch">The base branch name.</param>
/// <param name="State">The PR state (e.g. OPEN, open).</param>
public sealed record GitHubPullRequestInfo(int Number, string Url, string HeadBranch, string? BaseBranch, string State);
