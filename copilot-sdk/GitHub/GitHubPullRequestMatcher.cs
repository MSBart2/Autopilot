namespace Cyberpilot.GitHub;

internal static class GitHubPullRequestMatcher
{
    public static bool IsIssueBranch(string headRef, int issueNumber)
    {
        return headRef.Contains($"-{issueNumber}-", StringComparison.OrdinalIgnoreCase)
            || headRef.Contains($"issue-{issueNumber}", StringComparison.OrdinalIgnoreCase)
            || headRef.EndsWith($"-{issueNumber}", StringComparison.OrdinalIgnoreCase);
    }
}