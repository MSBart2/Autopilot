namespace Cyberpilot;

/// <summary>
/// Runs the Cyberpilot SDK pipeline for a single GitHub issue.
/// </summary>
public interface ICyberpilotRunner
{
    /// <summary>
    /// Runs Cyberpilot using the default text progress sink.
    /// </summary>
    /// <param name="request">The run request.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The typed run result.</returns>
    Task<CyberpilotRunResult> RunAsync(CyberpilotRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs Cyberpilot using a caller-provided progress sink.
    /// </summary>
    /// <param name="request">The run request.</param>
    /// <param name="progressSink">The progress sink receiving stage and stream events.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The typed run result.</returns>
    Task<CyberpilotRunResult> RunAsync(CyberpilotRunRequest request, ICyberpilotProgressSink progressSink, CancellationToken cancellationToken = default);
}
