using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed record BranchRoutingResult(PipelineStart Start, string? BranchName, string? PrUrl);

internal sealed class PipelineBranchCoordinator(
    CyberpilotOptions options,
    IGitHubIssueClient issueClient,
    IBranchProvisioner branchProvisioner,
    ICyberpilotProgressSink progressSink,
    PipelineConsoleWriter console)
{
    public async Task<BranchRoutingResult> ResolveStartAsync(PipelineStart start, CancellationToken cancellationToken)
    {
        return await FastForwardForExistingPullRequestAsync(start, cancellationToken);
    }

    public async Task<string> EnsureBranchAsync(PipelineStart start, CancellationToken cancellationToken)
    {
        return await EnsureBranchCoreAsync(start, cancellationToken);
    }

    public Task CloseIssueAsync(CancellationToken cancellationToken)
    {
        return issueClient.CloseIssueAsync(options.IssueNumber, cancellationToken);
    }

    private async Task<BranchRoutingResult> FastForwardForExistingPullRequestAsync(PipelineStart start, CancellationToken cancellationToken)
    {
        if (start.IsResume)
        {
            return new BranchRoutingResult(start, null, null);
        }

        try
        {
            var existingPr = await issueClient.FindPullRequestForIssueAsync(options.IssueNumber, cancellationToken);
            if (existingPr is null)
            {
                return new BranchRoutingResult(start, null, null);
            }

            progressSink.OnDispatch(DispatchType.Routing, $"Existing PR #{existingPr.Number} found for issue #{options.IssueNumber} — fast-forwarding to Review");
            console.WriteSuccess($"Found open PR #{existingPr.Number} ({existingPr.HeadBranch}). Skipping triage/plan/implement.");
            return new BranchRoutingResult(new PipelineStart(StageCatalog.IndexOf(StageCatalog.Review.Name), StageCatalog.Review, true), existingPr.HeadBranch, existingPr.Url);
        }
        catch (Exception ex)
        {
            console.WriteWarning($"Could not check for existing PRs: {ex.Message}");
            return new BranchRoutingResult(start, null, null);
        }
    }

    private async Task<string> EnsureBranchCoreAsync(PipelineStart start, CancellationToken cancellationToken)
    {
        var issue = await issueClient.GetIssueAsync(options.IssueNumber, cancellationToken);
        var branch = await branchProvisioner.EnsureBranchAsync(
            options.Repository ?? string.Empty,
            options.IssueNumber,
            issue?.Title ?? $"issue-{options.IssueNumber}",
            options.RepoRoot,
            cancellationToken);
        progressSink.OnBranchReady(branch.BranchName);

        if (!start.IsResume)
        {
            progressSink.OnDispatch(DispatchType.Branch, branch.WasCreated ? $"Created branch {branch.BranchName} for this issue" : $"Reusing existing branch {branch.BranchName}");
            console.WriteSuccess(branch.WasCreated
                ? $"Created branch {branch.BranchName}."
                : $"Using existing branch {branch.BranchName}.");
            await issueClient.CommentAsync(
                options.IssueNumber,
                $"SDK Cyberpilot branch ready: `{branch.BranchName}`. Planning and implementation will continue on this branch.",
                cancellationToken);
        }
        else
        {
            progressSink.OnDispatch(DispatchType.Branch, $"Resuming work on existing branch {branch.BranchName}");
            console.WriteSuccess($"Resuming on existing branch {branch.BranchName}.");
        }

        return branch.BranchName;
    }
}