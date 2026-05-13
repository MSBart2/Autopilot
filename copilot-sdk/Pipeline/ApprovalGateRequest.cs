namespace Cyberpilot.Pipeline;

internal sealed record ApprovalGateRequest(
    string Id,
    int IssueNumber,
    string StageName,
    GateTiming Timing,
    string Reason,
    string RequestedRole,
    string ResumeStageName,
    DateTimeOffset CreatedAt,
    ApprovalStatus Status = ApprovalStatus.Pending,
    ApprovalDecision? Decision = null)
{
    public bool IsPending => Status == ApprovalStatus.Pending;

    public ApprovalGateRequest Approve(string decidedBy, string? reason, DateTimeOffset decidedAt) =>
        Complete(ApprovalStatus.Approved, decidedBy, reason, decidedAt);

    public ApprovalGateRequest Reject(string decidedBy, string? reason, DateTimeOffset decidedAt) =>
        Complete(ApprovalStatus.Rejected, decidedBy, reason, decidedAt);

    private ApprovalGateRequest Complete(ApprovalStatus status, string decidedBy, string? reason, DateTimeOffset decidedAt)
    {
        if (!IsPending)
        {
            throw new InvalidOperationException($"Approval request '{Id}' has already been decided.");
        }

        if (string.IsNullOrWhiteSpace(decidedBy))
        {
            throw new ArgumentException("Approval decision requires an actor.", nameof(decidedBy));
        }

        return this with
        {
            Status = status,
            Decision = new ApprovalDecision(status, decidedBy.Trim(), string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), decidedAt),
        };
    }
}

internal sealed record ApprovalDecision(
    ApprovalStatus Status,
    string DecidedBy,
    string? Reason,
    DateTimeOffset DecidedAt);

internal enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}
