using GitHub.Copilot.SDK;
using System.Text;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Copilot;

internal sealed class CopilotStageRunner(string repoRoot, string model, ICyberpilotProgressSink progressSink, TextWriter error) : IStageRunner
{
    public async Task<StageResult> RunAsync(StageDefinition stage, string prompt, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await using var client = new CopilotClient(new CopilotClientOptions
        {
            Cwd = repoRoot,
        });

        await client.StartAsync(cancellationToken);
        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = model,
            Streaming = true,
            OnPermissionRequest = PermissionHandler.ApproveAll,
        });

        var streamed = new StringBuilder();
        using var subscription = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    progressSink.OnStreamDelta(delta.Data.DeltaContent);
                    streamed.Append(delta.Data.DeltaContent);
                    break;
                case SessionErrorEvent sessionError:
                    error.WriteLine(sessionError.Data.Message);
                    break;
            }
        });

        var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, timeout, cancellationToken);
        progressSink.OnMessage("step", string.Empty);

        var content = response?.Data.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            content = streamed.ToString();
        }

        int? inputTokens = null, outputTokens = null;
        try
        {
            var metrics = await session.Rpc.Usage.GetMetricsAsync(cancellationToken);
            inputTokens  = (int?)metrics.LastCallInputTokens;
            outputTokens = (int?)metrics.LastCallOutputTokens;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await error.WriteLineAsync($"[warn] Token usage capture failed: {ex.Message}");
        }

        return StageResult.Parse(content ?? string.Empty) with { InputTokens = inputTokens, OutputTokens = outputTokens };
    }
}
