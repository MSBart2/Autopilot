using Cyberpilot.Pipeline;

namespace Cyberpilot;

/// <summary>
/// Receives structured progress and token-streaming events from an Cyberpilot run.
/// </summary>
public interface ICyberpilotProgressSink
{
    /// <summary>
    /// Records that a pipeline stage has started.
    /// </summary>
    /// <param name="stage">The stage definition.</param>
    /// <param name="issueNumber">The GitHub issue number.</param>
    void OnStageStarted(StageDefinition stage, int issueNumber);

    /// <summary>
    /// Records that a pipeline stage has completed.
    /// </summary>
    /// <param name="stage">The stage definition.</param>
    /// <param name="result">The parsed stage result.</param>
    void OnStageCompleted(StageDefinition stage, StageResult result);

    /// <summary>
    /// Records that the feature branch has been provisioned.
    /// </summary>
    /// <param name="branchName">The name of the created or reused branch.</param>
    void OnBranchReady(string branchName);

    /// <summary>
    /// Records a structured pipeline message.
    /// </summary>
    /// <param name="level">The message level.</param>
    /// <param name="message">The message text.</param>
    void OnMessage(string level, string message);

    /// <summary>
    /// Records a streaming content delta from the Copilot SDK session.
    /// </summary>
    /// <param name="content">The streamed content.</param>
    void OnStreamDelta(string content);

    /// <summary>
    /// Records an orchestrator-level dispatch (routing decision, preflight, halt, etc.).
    /// </summary>
    /// <param name="type">The dispatch category (use <see cref="DispatchType"/> constants).</param>
    /// <param name="message">A short human-readable description of the event.</param>
    void OnDispatch(string type, string message);
}

/// <summary>
/// Constants for orchestrator dispatch event types.
/// </summary>
public static class DispatchType
{
    /// <summary>Preflight checks (model, labels).</summary>
    public const string Preflight = "preflight";

    /// <summary>Stage routing decisions (GO, STOP, DUPLICATE).</summary>
    public const string Routing = "routing";

    /// <summary>Branch provisioning.</summary>
    public const string Branch = "branch";

    /// <summary>Review loop decisions (changes_requested, cycle tracking).</summary>
    public const string ReviewLoop = "review_loop";

    /// <summary>Pipeline halted due to unexpected status.</summary>
    public const string Halt = "halt";

    /// <summary>Human approval or operator pause decision.</summary>
    public const string Approval = "approval";

    /// <summary>Deterministic policy gate evaluation.</summary>
    public const string Gate = "gate";

    /// <summary>Skip-deliver or early termination.</summary>
    public const string Skip = "skip";

    /// <summary>Issue closed after successful delivery.</summary>
    public const string IssueClosed = "issue_close";
}
