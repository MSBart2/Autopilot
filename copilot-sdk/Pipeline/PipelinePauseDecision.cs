namespace Cyberpilot.Pipeline;

/// <summary>
/// Provides context for deciding whether a pipeline should pause after a completed stage.
/// </summary>
/// <param name="CompletedStageName">The stage that just completed.</param>
/// <param name="IssueNumber">The GitHub issue number being processed.</param>
/// <param name="BranchName">The current feature branch name, when available.</param>
/// <param name="PullRequestUrl">The current pull request URL, when available.</param>
public sealed record PipelinePauseContext(
    string CompletedStageName,
    int IssueNumber,
    string? BranchName,
    string? PullRequestUrl);

/// <summary>
/// Describes whether pipeline execution should pause and optionally request human approval.
/// </summary>
/// <param name="ShouldPause">Whether execution should pause.</param>
/// <param name="Reason">The human-readable pause reason.</param>
/// <param name="ApprovalRequest">The approval request associated with the pause, when one is required.</param>
public sealed record PipelinePauseDecision(
    bool ShouldPause,
    string Reason,
    ApprovalGateRequest? ApprovalRequest = null)
{
    /// <summary>
    /// Creates a decision that allows the pipeline to continue.
    /// </summary>
    /// <returns>A continue decision.</returns>
    public static PipelinePauseDecision Continue() => new(false, string.Empty);

    /// <summary>
    /// Creates a decision that pauses pipeline execution.
    /// </summary>
    /// <param name="reason">The pause reason.</param>
    /// <param name="approvalRequest">The optional approval request associated with the pause.</param>
    /// <returns>A pause decision.</returns>
    public static PipelinePauseDecision Pause(string reason, ApprovalGateRequest? approvalRequest = null) =>
        new(true, string.IsNullOrWhiteSpace(reason) ? "Pipeline pause requested." : reason.Trim(), approvalRequest);
}
