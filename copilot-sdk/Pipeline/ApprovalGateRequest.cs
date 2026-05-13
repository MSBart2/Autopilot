namespace Cyberpilot.Pipeline;

/// <summary>
/// Represents a human approval request raised by a pipeline gate or pause decision.
/// </summary>
/// <param name="Id">The stable approval request identifier.</param>
/// <param name="IssueNumber">The GitHub issue number associated with the request.</param>
/// <param name="StageName">The stage that requested approval.</param>
/// <param name="Timing">The gate timing that requested approval.</param>
/// <param name="Reason">The human-readable approval reason.</param>
/// <param name="RequestedRole">The role expected to decide the approval.</param>
/// <param name="ResumeStageName">The stage to resume when approved.</param>
/// <param name="CreatedAt">The time the approval request was created.</param>
/// <param name="Status">The approval status.</param>
/// <param name="Decision">The recorded approval decision, when available.</param>
public sealed record ApprovalGateRequest(
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
    /// <summary>
    /// Gets whether the request is still awaiting a decision.
    /// </summary>
    public bool IsPending => Status == ApprovalStatus.Pending;

    /// <summary>
    /// Returns a copy marked as approved.
    /// </summary>
    /// <param name="decidedBy">The actor approving the request.</param>
    /// <param name="reason">The optional decision reason.</param>
    /// <param name="decidedAt">The decision timestamp.</param>
    /// <returns>The approved request.</returns>
    public ApprovalGateRequest Approve(string decidedBy, string? reason, DateTimeOffset decidedAt) =>
        Complete(ApprovalStatus.Approved, decidedBy, reason, decidedAt);

    /// <summary>
    /// Returns a copy marked as rejected.
    /// </summary>
    /// <param name="decidedBy">The actor rejecting the request.</param>
    /// <param name="reason">The optional decision reason.</param>
    /// <param name="decidedAt">The decision timestamp.</param>
    /// <returns>The rejected request.</returns>
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

/// <summary>
/// Represents the decision recorded for an approval request.
/// </summary>
/// <param name="Status">The final approval status.</param>
/// <param name="DecidedBy">The actor who made the decision.</param>
/// <param name="Reason">The optional decision reason.</param>
/// <param name="DecidedAt">The decision timestamp.</param>
public sealed record ApprovalDecision(
    ApprovalStatus Status,
    string DecidedBy,
    string? Reason,
    DateTimeOffset DecidedAt);

/// <summary>
/// Describes the lifecycle state of an approval request.
/// </summary>
public enum ApprovalStatus
{
    /// <summary>The approval request is awaiting a decision.</summary>
    Pending,

    /// <summary>The approval request was approved.</summary>
    Approved,

    /// <summary>The approval request was rejected.</summary>
    Rejected,

    /// <summary>The approval request was cancelled.</summary>
    Cancelled,
}
