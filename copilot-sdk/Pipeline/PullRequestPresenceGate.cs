using Cyberpilot.GitHub;

namespace Cyberpilot.Pipeline;

internal sealed class PullRequestPresenceGate(IGitHubIssueClient issueClient) : IPipelineGate
{
    public async Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default)
    {
        var issueNumber = context.ExecutionContext.Options.IssueNumber;
        var pullRequest = await issueClient.FindPullRequestForIssueAsync(issueNumber, cancellationToken);
        if (pullRequest is null)
        {
            return PipelineGateResult.Fail(
                $"No open pull request is linked to issue #{issueNumber}.",
                isRetryable: true,
                requiredActions: ["Create or link a pull request for this issue before continuing."]);
        }

        if (!pullRequest.State.Equals("OPEN", StringComparison.OrdinalIgnoreCase))
        {
            return PipelineGateResult.Fail(
                $"Pull request #{pullRequest.Number} is {pullRequest.State}, not OPEN.",
                isRetryable: true,
                requiredActions: [$"Reopen pull request #{pullRequest.Number} or create a new linked pull request."]);
        }

        return PipelineGateResult.Pass($"Open pull request #{pullRequest.Number} is linked: {pullRequest.Url}");
    }
}
