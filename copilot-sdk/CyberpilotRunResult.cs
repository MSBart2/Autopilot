using Cyberpilot.Pipeline;

namespace Cyberpilot;

/// <summary>
/// Represents the final result of an Cyberpilot SDK pipeline run.
/// </summary>
public sealed record CyberpilotRunResult(
    int ExitCode,
    string FinalStage,
    string Status,
    string? BranchName,
    string? PrUrl,
    string? Error,
    IReadOnlyList<StageResult> StageResults)
{
    /// <summary>
    /// Creates a result from a legacy integer exit code.
    /// </summary>
    /// <param name="exitCode">The process-style exit code.</param>
    /// <param name="finalStage">The last stage reached by the pipeline.</param>
    /// <param name="status">The normalized pipeline status.</param>
    /// <param name="branchName">The stable issue branch name, when one was provisioned.</param>
    /// <param name="error">The error message, when the pipeline failed.</param>
    /// <returns>A typed run result.</returns>
    public static CyberpilotRunResult FromExitCode(int exitCode, string finalStage, string status, string? branchName = null, string? error = null)
    {
        return new CyberpilotRunResult(exitCode, finalStage, status, branchName, null, error, []);
    }
}
