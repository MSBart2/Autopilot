using GitHub.Copilot.SDK;
using System.Text;
using Cyberpilot.GitHub;
using Cyberpilot.Pipeline;
using Microsoft.Extensions.AI;

namespace Cyberpilot.Copilot;

internal sealed class CopilotStageRunner(string repoRoot, ICyberpilotProgressSink defaultProgressSink, TextWriter error) : IStageRunner
{
    public async Task<StageResult> RunAsync(
        StageDefinition stage,
        BuiltPrompt builtPrompt,
        TimeSpan timeout,
        string model,
        PipelineExecutionContext context,
        ICyberpilotProgressSink progressSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sink = progressSink ?? defaultProgressSink;

        await using var client = new CopilotClient(new CopilotClientOptions
        {
            Cwd = repoRoot,
        });

        var toolProvider = new PipelineContextToolProvider(context, stage, new GitHubCli(repoRoot, context.Repository));
        var toolPolicy = new StageToolPolicyHooks(stage, context);
        var attempt = context.StageHistory.Count(summary => summary.StageName.Equals(stage.Name, StringComparison.OrdinalIgnoreCase));
        var sessionId = StageSessionIdentity.Create(context.RunId, stage.Name, attempt);

        await client.StartAsync(cancellationToken);
        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            SessionId = sessionId,
            Model = model,
            Streaming = true,
            OnPermissionRequest = PermissionHandler.ApproveAll,
            Tools = toolProvider.CreateTools(),
            Hooks = toolPolicy.CreateHooks(),
            SystemMessage = builtPrompt.SystemMessageContent is not null
                ? new SystemMessageConfig
                {
                    Mode = builtPrompt.SystemMessageMode == HarnessSystemMessageMode.Replace
                        ? SystemMessageMode.Replace
                        : SystemMessageMode.Append,
                    Content = builtPrompt.SystemMessageContent,
                }
                : null,
        });

        var streamed = new StringBuilder();
        var metricsCollector = new StageExecutionMetricsCollector(model, stage.Name);
        using var subscription = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantTurnStartEvent turnStart:
                    metricsCollector.RecordTurnStart(turnStart.Data);
                    break;
                case AssistantUsageEvent usage:
                    metricsCollector.RecordUsage(usage.Data);
                    break;
                case ToolExecutionStartEvent toolStart:
                    metricsCollector.RecordToolExecutionStart(toolStart.Data);
                    break;
                case ToolExecutionCompleteEvent toolComplete:
                    metricsCollector.RecordToolExecutionComplete(toolComplete.Data);
                    break;
                case AssistantMessageDeltaEvent delta:
                    sink.OnStreamDelta(delta.Data.DeltaContent);
                    streamed.Append(delta.Data.DeltaContent);
                    break;
                case SessionErrorEvent sessionError:
                    metricsCollector.RecordSessionError(sessionError.Data);
                    error.WriteLine(sessionError.Data.Message);
                    break;
                case SessionIdleEvent idle:
                    metricsCollector.RecordSessionIdle(idle.Data);
                    break;
            }
        });

        var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = builtPrompt.UserMessage }, timeout, cancellationToken);
        sink.OnMessage("step", string.Empty);

        var content = response?.Data.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            content = streamed.ToString();
        }

        int? inputTokens = null, outputTokens = null;
        try
        {
            var metrics = await session.Rpc.Usage.GetMetricsAsync(cancellationToken);
            metricsCollector.ApplyFinalUsageMetrics(
                metrics.CurrentModel,
                metrics.LastCallInputTokens,
                metrics.LastCallOutputTokens,
                metrics.TotalApiDurationMs,
                metrics.TotalPremiumRequestCost);
            inputTokens  = (int?)metrics.LastCallInputTokens;
            outputTokens = (int?)metrics.LastCallOutputTokens;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await error.WriteLineAsync($"[warn] Token usage capture failed: {ex.Message}");
        }

        var executionMetrics = metricsCollector.Build();
        return StageResult.Parse(content ?? string.Empty) with
        {
            InputTokens = executionMetrics.InputTokens ?? inputTokens,
            OutputTokens = executionMetrics.OutputTokens ?? outputTokens,
            Metrics = executionMetrics,
            SdkSessionId = sessionId,
        };
    }
}
