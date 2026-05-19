namespace Cyberpilot.Pipeline;

internal sealed class BufferedStageProgressSink : ICyberpilotProgressSink
{
    private readonly List<string> messages = [];
    private readonly StringWriter stream = new();

    public IReadOnlyList<string> Messages => messages;

    public string StreamedOutput => stream.ToString();

    public void OnStageStarted(StageDefinition stage, int issueNumber)
    {
    }

    public void OnStageCompleted(StageDefinition stage, StageResult result)
    {
    }

    public void OnBranchReady(string branchName)
    {
    }

    public void OnApprovalRequested(ApprovalGateRequest request)
    {
    }

    public void OnMessage(string level, string message)
    {
        messages.Add($"[{level}] {message}");
    }

    public void OnStreamDelta(string content)
    {
        stream.Write(content);
    }

    public void OnDispatch(string type, string message)
    {
        messages.Add($"[dispatch:{type}] {message}");
    }
}
