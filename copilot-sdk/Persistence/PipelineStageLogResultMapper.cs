using System.Text.Json;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Persistence;

/// <summary>
/// Applies a completed stage result to its persisted stage-log row.
/// </summary>
public static class PipelineStageLogResultMapper
{
    /// <summary>
    /// Copies result status, metrics, session metadata, cost, and serialized result data onto a stage log.
    /// </summary>
    public static void Apply(PipelineStageLog log, StageResult result, string configuredModel, DateTime completedAtUtc)
    {
        log.Status = result.Status;
        log.CompletedAt = completedAtUtc;
        log.InputTokens = result.InputTokens;
        log.OutputTokens = result.OutputTokens;
        ApplyMetrics(log, result, configuredModel);
        ApplySessionMetadata(log, result, completedAtUtc);
        log.EstimatedCostUsd = ModelPricingService.Estimate(ResolveCostModel(result, log, configuredModel), result.InputTokens, result.OutputTokens);
        log.StageResultJson = JsonSerializer.Serialize(result);
        log.StageResultContractVersion = FirstNonEmpty(result.ContractVersion, PipelineDefinitionDefaults.ContractVersion);
    }

    private static void ApplyMetrics(PipelineStageLog log, StageResult result, string configuredModel)
    {
        var metrics = result.Metrics;
        ApplyModelMetrics(log, result, configuredModel, metrics);
        ApplyTokenMetrics(log, metrics);
        ApplyExecutionMetrics(log, metrics);
        ApplyToolMetrics(log, metrics);
    }

    private static void ApplyModelMetrics(PipelineStageLog log, StageResult result, string configuredModel, StageExecutionMetrics? metrics)
    {
        log.Model = FirstNonEmpty(metrics?.Model, configuredModel);
        log.ConfiguredModel = FirstNonEmpty(result.ConfiguredModel, configuredModel);
        log.SelectedModel = FirstNonEmpty(result.SelectedModel, log.Model);
        log.FallbackModel = result.FallbackModel;
        log.FallbackReason = result.FallbackReason;
    }

    private static void ApplyTokenMetrics(PipelineStageLog log, StageExecutionMetrics? metrics)
    {
        log.CacheReadTokens = metrics?.CacheReadTokens;
        log.CacheWriteTokens = metrics?.CacheWriteTokens;
        log.ReasoningTokens = metrics?.ReasoningTokens;
        log.PremiumRequestCost = metrics?.PremiumRequestCost;
    }

    private static void ApplyExecutionMetrics(PipelineStageLog log, StageExecutionMetrics? metrics)
    {
        log.DurationMs = metrics?.DurationMs;
        log.TurnCount = metrics?.TurnCount;
        log.SessionErrorCount = metrics?.SessionErrorCount;
        log.ReachedIdle = metrics?.ReachedIdle;
        log.WasAborted = metrics?.WasAborted;
    }

    private static void ApplyToolMetrics(PipelineStageLog log, StageExecutionMetrics? metrics)
    {
        log.ToolCallCount = metrics?.ToolCallCount;
        log.FailedToolCallCount = metrics?.FailedToolCallCount;
        log.ProviderCallIds = JoinIds(metrics?.ProviderCallIds);
        log.ApiCallIds = JoinIds(metrics?.ApiCallIds);
    }

    private static void ApplySessionMetadata(PipelineStageLog log, StageResult result, DateTime completedAtUtc)
    {
        log.SdkSessionId = result.SdkSessionId;
        var resume = StageSessionResumePolicy.Evaluate(result, completedAtUtc);
        log.SessionState = resume.SessionState;
        log.ResumeEligibility = resume.ResumeEligibility;
        log.ResumeBlockedReason = resume.ResumeBlockedReason;
        log.SessionCleanupAfter = resume.SessionCleanupAfter;
    }

    private static string ResolveCostModel(StageResult result, PipelineStageLog log, string configuredModel)
        => FirstNonEmpty(result.SelectedModel, log.Model, configuredModel);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string? JoinIds(IReadOnlyList<string>? values)
        => values is null || values.Count == 0 ? null : string.Join(",", values);
}
