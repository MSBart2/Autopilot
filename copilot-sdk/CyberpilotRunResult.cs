using Cyberpilot.Pipeline;

namespace Cyberpilot;

/// <summary>
/// Represents the final result of an Cyberpilot SDK pipeline run.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "PrUrl is stored and transferred as a string; Uri conversion is unnecessary overhead.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "PrUrl is stored and transferred as a string; Uri conversion is unnecessary overhead.")]
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
    /// <param name="prUrl">The pull request URL, when known.</param>
    /// <param name="error">The error message, when the pipeline failed.</param>
    /// <param name="stageResults">The parsed stage results collected during the run.</param>
    /// <returns>A typed run result.</returns>
    public static CyberpilotRunResult FromExitCode(
        int exitCode,
        string finalStage,
        string status,
        string? branchName = null,
        string? prUrl = null,
        string? error = null,
        IReadOnlyList<StageResult>? stageResults = null)
    {
        return new CyberpilotRunResult(exitCode, finalStage, status, branchName, prUrl, error, stageResults ?? []);
    }
}
