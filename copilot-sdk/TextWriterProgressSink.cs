using Cyberpilot.Pipeline;

namespace Cyberpilot;

/// <summary>
/// Writes Cyberpilot progress events to text writers for CLI compatibility.
/// </summary>
public sealed class TextWriterProgressSink(TextWriter output, TextWriter error) : ICyberpilotProgressSink
{
    /// <inheritdoc />
    public void OnStageStarted(StageDefinition stage, int issueNumber)
    {
        output.WriteLine();
        output.WriteLine("============================================================");
        output.WriteLine($"Stage: {stage.DisplayName}");
        output.WriteLine("============================================================");
        output.WriteLine($"  {"Issue",-14}: #{issueNumber}");
        output.WriteLine($"  {"Label",-14}: {stage.Label}");
    }

    /// <inheritdoc />
    public void OnStageCompleted(StageDefinition stage, StageResult result)
    {
        output.WriteLine($"[ ok ] Stage {stage.DisplayName} complete");
        output.WriteLine($"  {"Status",-14}: {result.Status}");
        output.WriteLine($"  {"Decision",-14}: {result.Decision}");
    }

    /// <inheritdoc />
    public void OnBranchReady(string branchName)
    {
        output.WriteLine($"[branch] {branchName}");
    }

    /// <inheritdoc />
    public void OnApprovalRequested(ApprovalGateRequest request)
    {
        output.WriteLine($"[approval] {request.Id} requested for {request.RequestedRole}: {request.Reason}");
    }

    /// <inheritdoc />
    public void OnMessage(string level, string message)
    {
        var writer = level.Equals("fail", StringComparison.OrdinalIgnoreCase) ? error : output;
        writer.WriteLine($"[{level}] {message}");
    }

    /// <inheritdoc />
    public void OnStreamDelta(string content)
    {
        output.Write(content);
    }

    /// <inheritdoc />
    public void OnDispatch(string type, string message)
    {
        output.WriteLine($"[dispatch:{type}] {message}");
    }
}
