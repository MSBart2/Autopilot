using Cyberpilot.Pipeline;

namespace Cyberpilot;

/// <summary>
/// Forwards Cyberpilot progress events to multiple sinks.
/// </summary>
public sealed class CompositeProgressSink(params ICyberpilotProgressSink[] sinks) : ICyberpilotProgressSink
{
    /// <inheritdoc />
    public void OnStageStarted(StageDefinition stage, int issueNumber)
    {
        foreach (var sink in sinks)
        {
            sink.OnStageStarted(stage, issueNumber);
        }
    }

    /// <inheritdoc />
    public void OnStageCompleted(StageDefinition stage, StageResult result)
    {
        foreach (var sink in sinks)
        {
            sink.OnStageCompleted(stage, result);
        }
    }

    /// <inheritdoc />
    public void OnBranchReady(string branchName)
    {
        foreach (var sink in sinks)
        {
            sink.OnBranchReady(branchName);
        }
    }

    /// <inheritdoc />
    public void OnMessage(string level, string message)
    {
        foreach (var sink in sinks)
        {
            sink.OnMessage(level, message);
        }
    }

    /// <inheritdoc />
    public void OnStreamDelta(string content)
    {
        foreach (var sink in sinks)
        {
            sink.OnStreamDelta(content);
        }
    }

    /// <inheritdoc />
    public void OnDispatch(string type, string message)
    {
        foreach (var sink in sinks)
        {
            sink.OnDispatch(type, message);
        }
    }
}
