using Cyberpilot.Copilot;
using GitHub.Copilot.SDK;

namespace Cyberpilot.Sdk.Tests;

public sealed class StageExecutionMetricsCollectorTests
{
    [Fact]
    public void Build_AggregatesStreamingEvents()
    {
        var collector = new StageExecutionMetricsCollector("configured-model");

        collector.RecordTurnStart(new AssistantTurnStartData { TurnId = "turn-1" });
        collector.RecordTurnStart(new AssistantTurnStartData { TurnId = "turn-2" });
        collector.RecordUsage(new AssistantUsageData
        {
            Model = "actual-model",
            InputTokens = 100,
            OutputTokens = 25,
            CacheReadTokens = 10,
            CacheWriteTokens = 2,
            ReasoningTokens = 3,
            Cost = 1.5,
            Duration = 250,
            ProviderCallId = "provider-1",
            ApiCallId = "api-1",
        });
        collector.RecordUsage(new AssistantUsageData
        {
            Model = string.Empty,
            InputTokens = 50,
            OutputTokens = 5,
            Cost = 0.5,
            Duration = 125,
            ProviderCallId = "provider-2",
            ApiCallId = "api-2",
        });
        collector.RecordToolExecutionStart(new ToolExecutionStartData { ToolCallId = "tool-1", ToolName = "read_file" });
        collector.RecordToolExecutionStart(new ToolExecutionStartData { ToolCallId = "tool-2", ToolName = "run_in_terminal" });
        collector.RecordToolExecutionComplete(new ToolExecutionCompleteData { ToolCallId = "tool-1", Success = true });
        collector.RecordToolExecutionComplete(new ToolExecutionCompleteData { ToolCallId = "tool-2", Success = false });
        collector.RecordSessionError(new SessionErrorData { ErrorType = "provider", Message = "failed", ProviderCallId = "provider-2" });
        collector.RecordSessionIdle(new SessionIdleData { Aborted = true });

        var metrics = collector.Build();

        Assert.Equal("actual-model", metrics.Model);
        Assert.Equal(150, metrics.InputTokens);
        Assert.Equal(30, metrics.OutputTokens);
        Assert.Equal(10, metrics.CacheReadTokens);
        Assert.Equal(2, metrics.CacheWriteTokens);
        Assert.Equal(3, metrics.ReasoningTokens);
        Assert.Equal(2.0, metrics.PremiumRequestCost);
        Assert.Equal(375, metrics.DurationMs);
        Assert.Equal(2, metrics.TurnCount);
        Assert.Equal(2, metrics.ToolCallCount);
        Assert.Equal(1, metrics.FailedToolCallCount);
        Assert.Equal(1, metrics.SessionErrorCount);
        Assert.True(metrics.ReachedIdle);
        Assert.True(metrics.WasAborted);
        Assert.Equal(["provider-1", "provider-2"], metrics.ProviderCallIds);
        Assert.Equal(["api-1", "api-2"], metrics.ApiCallIds);
    }

    [Fact]
    public void ApplyFinalUsageMetrics_FillsMissingUsageWithoutOverwritingStreamingUsage()
    {
        var collector = new StageExecutionMetricsCollector("configured-model");
        collector.RecordUsage(new AssistantUsageData
        {
            Model = string.Empty,
            InputTokens = 100,
            OutputTokens = 25,
        });

        collector.ApplyFinalUsageMetrics("fallback-model", 900, 800, TimeSpan.FromMilliseconds(1250), 3);

        var metrics = collector.Build();

        Assert.Equal("fallback-model", metrics.Model);
        Assert.Equal(100, metrics.InputTokens);
        Assert.Equal(25, metrics.OutputTokens);
        Assert.Equal(1250, metrics.DurationMs);
        Assert.Equal(3, metrics.PremiumRequestCost);
    }

    [Fact]
    public void Build_UsesConfiguredModelWhenNoUsageModelWasReported()
    {
        var collector = new StageExecutionMetricsCollector("configured-model");

        var metrics = collector.Build();

        Assert.Equal("configured-model", metrics.Model);
    }
}
