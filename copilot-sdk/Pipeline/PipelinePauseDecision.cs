namespace Cyberpilot.Pipeline;

internal sealed record PipelinePauseContext(
    string CompletedStageName,
    int IssueNumber,
    string? BranchName,
    string? PullRequestUrl);

internal sealed record PipelinePauseDecision(
    bool ShouldPause,
    string Reason,
    ApprovalGateRequest? ApprovalRequest = null)
{
    public static PipelinePauseDecision Continue() => new(false, string.Empty);

    public static PipelinePauseDecision Pause(string reason, ApprovalGateRequest? approvalRequest = null) =>
        new(true, string.IsNullOrWhiteSpace(reason) ? "Pipeline pause requested." : reason.Trim(), approvalRequest);
}
