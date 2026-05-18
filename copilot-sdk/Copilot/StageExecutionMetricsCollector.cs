using Cyberpilot.Pipeline;
using GitHub.Copilot.SDK;

namespace Cyberpilot.Copilot;

internal sealed class StageExecutionMetricsCollector(string configuredModel)
{
    private readonly HashSet<string> providerCallIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> apiCallIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> toolNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> toolArgs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FailedToolCallRecord> failedToolCalls = [];
    private int? inputTokens;
    private int? outputTokens;
    private int? cacheReadTokens;
    private int? cacheWriteTokens;
    private int? reasoningTokens;
    private double? premiumRequestCost;
    private double? durationMs;
    private string? model;

    public int TurnCount { get; private set; }

    public int ToolCallCount { get; private set; }

    public int FailedToolCallCount { get; private set; }

    public int SessionErrorCount { get; private set; }

    public bool ReachedIdle { get; private set; }

    public bool WasAborted { get; private set; }

    public void RecordTurnStart(AssistantTurnStartData data)
    {
        TurnCount++;
    }

    public void RecordUsage(AssistantUsageData data)
    {
        model = string.IsNullOrWhiteSpace(data.Model) ? model : data.Model;
        inputTokens = AddTokens(inputTokens, data.InputTokens);
        outputTokens = AddTokens(outputTokens, data.OutputTokens);
        cacheReadTokens = AddTokens(cacheReadTokens, data.CacheReadTokens);
        cacheWriteTokens = AddTokens(cacheWriteTokens, data.CacheWriteTokens);
        reasoningTokens = AddTokens(reasoningTokens, data.ReasoningTokens);
        premiumRequestCost = AddNullable(premiumRequestCost, data.Cost);
        durationMs = AddNullable(durationMs, data.Duration);
        AddId(providerCallIds, data.ProviderCallId);
        AddId(apiCallIds, data.ApiCallId);
    }

    public void RecordToolExecutionStart(ToolExecutionStartData data)
    {
        ToolCallCount++;
        if (!string.IsNullOrWhiteSpace(data.ToolCallId) && !string.IsNullOrWhiteSpace(data.ToolName))
        {
            toolNames[data.ToolCallId] = data.ToolName;
            toolArgs[data.ToolCallId] = data.Arguments is null ? null
                : System.Text.Json.JsonSerializer.Serialize(data.Arguments);
        }
    }

    public void RecordToolExecutionComplete(ToolExecutionCompleteData data)
    {
        if (!data.Success)
        {
            FailedToolCallCount++;
            var callId = data.ToolCallId ?? string.Empty;
            toolNames.TryGetValue(callId, out var toolName);
            toolArgs.TryGetValue(callId, out var args);
            failedToolCalls.Add(new FailedToolCallRecord(
                callId,
                toolName,
                args,
                data.Error?.Code,
                data.Error?.Message));
        }
    }

    public void RecordSessionError(SessionErrorData data)
    {
        SessionErrorCount++;
        AddId(providerCallIds, data.ProviderCallId);
    }

    public void RecordSessionIdle(SessionIdleData data)
    {
        ReachedIdle = true;
        WasAborted = data.Aborted == true;
    }

    public void ApplyFinalUsageMetrics(
        string? currentModel,
        long lastCallInputTokens,
        long lastCallOutputTokens,
        TimeSpan totalApiDuration,
        double totalPremiumRequestCost)
    {
        model = string.IsNullOrWhiteSpace(currentModel) ? model : currentModel;
        inputTokens ??= ToTokenCount(lastCallInputTokens);
        outputTokens ??= ToTokenCount(lastCallOutputTokens);
        premiumRequestCost ??= totalPremiumRequestCost;
        durationMs ??= totalApiDuration.TotalMilliseconds;
    }

    public StageExecutionMetrics Build()
    {
        return new StageExecutionMetrics(
            string.IsNullOrWhiteSpace(model) ? configuredModel : model,
            inputTokens,
            outputTokens,
            cacheReadTokens,
            cacheWriteTokens,
            reasoningTokens,
            premiumRequestCost,
            durationMs,
            TurnCount,
            ToolCallCount,
            FailedToolCallCount,
            SessionErrorCount,
            ReachedIdle,
            WasAborted,
            providerCallIds.Count == 0 ? null : providerCallIds.Order().ToArray(),
            apiCallIds.Count == 0 ? null : apiCallIds.Order().ToArray(),
            failedToolCalls.Count == 0 ? null : failedToolCalls.AsReadOnly());
    }

    private static int? AddTokens(int? current, double? value)
    {
        var tokenCount = ToTokenCount(value);
        if (!tokenCount.HasValue)
        {
            return current;
        }

        return (current ?? 0) + tokenCount.Value;
    }

    private static int? ToTokenCount(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
    }

    private static int? ToTokenCount(long value)
    {
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static double? AddNullable(double? current, double? value)
    {
        if (!value.HasValue)
        {
            return current;
        }

        return (current ?? 0) + value.Value;
    }

    private static void AddId(HashSet<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }
}