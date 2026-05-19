using Cyberpilot.Persistence;

namespace Cyberpilot.Pipeline;

/// <summary>
/// Evaluates whether persisted SDK sessions can be safely resumed.
/// </summary>
public static class StageSessionResumePolicy
{
    /// <summary>
    /// Evaluates the final stage result and determines persisted session resume eligibility.
    /// </summary>
    /// <param name="result">The completed stage result.</param>
    /// <param name="completedAtUtc">The UTC completion time used as the retention anchor.</param>
    /// <returns>The session lifecycle and resume decision.</returns>
    public static StageSessionResumeDecision Evaluate(StageResult result, DateTime completedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(result.SdkSessionId))
        {
            return StageSessionResumeDecision.Blocked("Stage did not report a stable SDK session ID.", completedAtUtc);
        }

        if (result.Metrics?.WasAborted == true)
        {
            return StageSessionResumeDecision.Blocked("SDK session reported an abort; resume would be unsafe.", completedAtUtc);
        }

        if (result.Metrics?.SessionErrorCount > 0 && !StageStatus.IsGo(result))
        {
            return StageSessionResumeDecision.Blocked("SDK session reported errors before a successful stage result.", completedAtUtc);
        }

        if (StageStatus.IsGo(result) && result.Metrics?.ReachedIdle == true)
        {
            return StageSessionResumeDecision.NotApplicable("Stage completed successfully and reached idle.", completedAtUtc);
        }

        return StageSessionResumeDecision.Eligible(completedAtUtc);
    }

    /// <summary>
    /// Evaluates an interrupted stage log and determines whether its SDK session can be resumed.
    /// </summary>
    /// <param name="log">The interrupted stage log.</param>
    /// <param name="completedAtUtc">The UTC recovery time used as the retention anchor.</param>
    /// <returns>The session lifecycle and resume decision.</returns>
    public static StageSessionResumeDecision Interrupted(PipelineStageLog log, DateTime completedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(log.SdkSessionId))
        {
            return StageSessionResumeDecision.Blocked("Interrupted stage has no stable SDK session ID.", completedAtUtc);
        }

        if (log.WasAborted == true)
        {
            return StageSessionResumeDecision.Blocked("Interrupted SDK session reported an abort before recovery.", completedAtUtc);
        }

        return StageSessionResumeDecision.Eligible(completedAtUtc);
    }

}

/// <summary>
/// Describes persisted SDK session lifecycle and resume eligibility.
/// </summary>
public sealed record StageSessionResumeDecision(
    string SessionState,
    string ResumeEligibility,
    string? ResumeBlockedReason,
    DateTime SessionCleanupAfter)
{
    /// <summary>
    /// Creates a decision indicating the SDK session is resumable.
    /// </summary>
    /// <param name="completedAtUtc">The UTC completion or recovery time used as the retention anchor.</param>
    /// <returns>A resumable session decision.</returns>
    public static StageSessionResumeDecision Eligible(DateTime completedAtUtc)
        => new("idle", "eligible", null, completedAtUtc.AddDays(7));

    /// <summary>
    /// Creates a decision indicating resume is blocked.
    /// </summary>
    /// <param name="reason">The human-readable reason resume is unsafe.</param>
    /// <param name="completedAtUtc">The UTC completion or recovery time used as the retention anchor.</param>
    /// <returns>A blocked resume decision.</returns>
    public static StageSessionResumeDecision Blocked(string reason, DateTime completedAtUtc)
        => new("blocked", "blocked", reason, completedAtUtc.AddDays(7));

    /// <summary>
    /// Creates a decision indicating resume does not apply to the completed stage.
    /// </summary>
    /// <param name="reason">The human-readable reason resume is not applicable.</param>
    /// <param name="completedAtUtc">The UTC completion time used as the retention anchor.</param>
    /// <returns>A non-applicable resume decision.</returns>
    public static StageSessionResumeDecision NotApplicable(string reason, DateTime completedAtUtc)
        => new("completed", "not_applicable", reason, completedAtUtc.AddDays(7));
}
