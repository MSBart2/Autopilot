using System.Text;
using System.Text.Json;
using Cyberpilot;
using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Persists Cyberpilot progress and streams it to dashboard clients.
/// </summary>
public sealed class SignalRProgressSink(
    string runId,
    string model,
    int issueNumber,
    CyberpilotDbContext dbContext,
    IHubContext<PipelineHub> hubContext,
    ILogger logger,
    string? retryStageName = null,
    string? retryReason = null) : ICyberpilotProgressSink
{
    private readonly StringBuilder buffer = new();
    private PipelineStageLog? currentLog;
    private bool retryReasonApplied;

    /// <inheritdoc />
    public void OnStageStarted(StageDefinition stage, int issueNumber)
    {
        FlushBufferAsync().GetAwaiter().GetResult();
        var retryCount = dbContext.PipelineStageLogs
            .Count(log => log.RunId == runId && log.StageName == stage.Name);
        currentLog = new PipelineStageLog
        {
            RunId = runId,
            StageName = stage.Name,
            Status = "Running",
            StartedAt = DateTime.UtcNow,
            RetryCount = retryCount,
        };

        var stageRetryReason = string.Empty;
        if (!retryReasonApplied
            && !string.IsNullOrWhiteSpace(retryReason)
            && stage.Name.Equals(retryStageName, StringComparison.OrdinalIgnoreCase))
        {
            stageRetryReason = retryReason.Trim();
            currentLog.RetryReason = stageRetryReason;
            retryReasonApplied = true;
        }

        dbContext.PipelineStageLogs.Add(currentLog);

        var run = dbContext.PipelineRuns.Find(runId);
        if (run is not null)
        {
            run.CurrentStage = stage.Name;
            if (run.Status != "Pausing")
                run.Status = "Running";
        }

        dbContext.SaveChanges();
        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("stageStarted", new
        {
            runId,
            issueNumber,
            stage = stage.Name,
            retryReason = string.IsNullOrWhiteSpace(stageRetryReason) ? null : stageRetryReason,
        }).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnStageCompleted(StageDefinition stage, StageResult result)
    {
        FlushBufferAsync().GetAwaiter().GetResult();
        var estimatedCost = ModelPricingService.Estimate(model, result.InputTokens, result.OutputTokens);
        if (currentLog is not null)
        {
            currentLog.Status = result.Status;
            currentLog.CompletedAt = DateTime.UtcNow;
            currentLog.InputTokens = result.InputTokens;
            currentLog.OutputTokens = result.OutputTokens;
            currentLog.EstimatedCostUsd = estimatedCost;
            ApplyMetrics(currentLog, result, model);
            currentLog.StageResultJson = JsonSerializer.Serialize(result);
            currentLog.StageResultContractVersion = string.IsNullOrWhiteSpace(result.ContractVersion)
                ? PipelineDefinitionDefaults.ContractVersion
                : result.ContractVersion;
            dbContext.PipelineArtifacts.AddRange(PipelineArtifact.FromStageResult(runId, stage.Name, currentLog, result));
            dbContext.PipelineEvidence.AddRange(PipelineEvidence.FromStageResult(runId, stage.Name, currentLog, result));
        }

        dbContext.SaveChanges();
        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("stageCompleted", new
        {
            runId,
            issueNumber,
            stage = stage.Name,
            result.Status,
            result.Decision,
            inputTokens = result.InputTokens,
            outputTokens = result.OutputTokens,
            estimatedCostUsd = estimatedCost,
            model = currentLog?.Model,
            turnCount = currentLog?.TurnCount,
            toolCallCount = currentLog?.ToolCallCount,
            failedToolCallCount = currentLog?.FailedToolCallCount,
            durationMs = currentLog?.DurationMs,
            sessionErrorCount = currentLog?.SessionErrorCount,
        }).GetAwaiter().GetResult();
    }

    private static void ApplyMetrics(PipelineStageLog log, StageResult result, string configuredModel)
    {
        var metrics = result.Metrics;
        log.Model = string.IsNullOrWhiteSpace(metrics?.Model) ? configuredModel : metrics.Model;
        log.ConfiguredModel = string.IsNullOrWhiteSpace(result.ConfiguredModel) ? configuredModel : result.ConfiguredModel;
        log.SelectedModel = string.IsNullOrWhiteSpace(result.SelectedModel) ? log.Model : result.SelectedModel;
        log.FallbackModel = result.FallbackModel;
        log.FallbackReason = result.FallbackReason;
        log.CacheReadTokens = metrics?.CacheReadTokens;
        log.CacheWriteTokens = metrics?.CacheWriteTokens;
        log.ReasoningTokens = metrics?.ReasoningTokens;
        log.PremiumRequestCost = metrics?.PremiumRequestCost;
        log.DurationMs = metrics?.DurationMs;
        log.TurnCount = metrics?.TurnCount;
        log.ToolCallCount = metrics?.ToolCallCount;
        log.FailedToolCallCount = metrics?.FailedToolCallCount;
        log.SessionErrorCount = metrics?.SessionErrorCount;
        log.ReachedIdle = metrics?.ReachedIdle;
        log.WasAborted = metrics?.WasAborted;
        log.ProviderCallIds = JoinIds(metrics?.ProviderCallIds);
        log.ApiCallIds = JoinIds(metrics?.ApiCallIds);
    }

    private static string? JoinIds(IReadOnlyList<string>? values)
    {
        return values is null || values.Count == 0 ? null : string.Join(",", values);
    }

    /// <inheritdoc />
    public void OnBranchReady(string branchName)
    {
        var run = dbContext.PipelineRuns.Find(runId);
        if (run is not null)
        {
            run.BranchName = branchName;
            AddBranchEvidence(branchName);
            dbContext.SaveChanges();
        }

        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("branchReady", new { runId, branchName }).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnApprovalRequested(ApprovalGateRequest request)
    {
        if (dbContext.PipelineApprovals.Find(request.Id) is null)
        {
            var approval = PipelineApproval.FromRequest(runId, request);
            dbContext.PipelineApprovals.Add(approval);
            dbContext.PipelineEvidence.Add(PipelineEvidence.FromApprovalRequest(approval));
            dbContext.SaveChanges();
        }

        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("approvalRequested", new
        {
            runId,
            approvalId = request.Id,
            issueNumber = request.IssueNumber,
            stage = request.StageName,
            timing = request.Timing.ToString(),
            reason = request.Reason,
            requestedRole = request.RequestedRole,
            resumeStage = request.ResumeStageName,
            createdAt = request.CreatedAt.ToString("o"),
        }).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnMessage(string level, string message)
    {
        logger.LogInformation("Cyberpilot {Level}: {Message}", level, message);
        AppendLine($"[{level}] {message}");
        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("message", new { runId, level, message }).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnStreamDelta(string content)
    {
        buffer.Append(content);
        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("streamDelta", new { runId, content }).GetAwaiter().GetResult();
        if (buffer.Length > 4096 || content.Contains('\n', StringComparison.Ordinal))
        {
            FlushBufferAsync().GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public void OnDispatch(string type, string message)
    {
        AddDeliveryEvidence(type, message);
        AddGateEvidence(type, message);
        AddRepositoryProfileEvidence(type, message);
        var dispatch = new Cyberpilot.Persistence.PipelineDispatch
        {
            RunId = runId,
            Type = type,
            Message = message,
        };
        dbContext.PipelineDispatches.Add(dispatch);
        dbContext.SaveChanges();
        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("cyberpilotDispatch", new { runId, type, message, timestamp = dispatch.CreatedAt.ToString("o") }).GetAwaiter().GetResult();
    }

    private void AddDeliveryEvidence(string type, string message)
    {
        var evidence = PipelineEvidence.FromDeliveryDispatch(runId, type, message);
        if (evidence is null)
        {
            return;
        }

        dbContext.PipelineEvidence.Add(evidence);
    }

    private void AddGateEvidence(string type, string message)
    {
        var evidence = PipelineEvidence.FromGateDispatch(runId, type, message);
        if (evidence is null)
        {
            return;
        }

        dbContext.PipelineEvidence.Add(evidence);
    }

    private void AddRepositoryProfileEvidence(string type, string message)
    {
        var evidence = PipelineEvidence.FromRepositoryProfileDispatch(runId, type, message);
        if (evidence is null)
        {
            return;
        }

        dbContext.PipelineEvidence.Add(evidence);
    }

    private void AddBranchEvidence(string branchName)
    {
        if (dbContext.PipelineEvidence.Any(evidence => evidence.RunId == runId && evidence.Kind == "branch-reference" && evidence.Name == branchName))
        {
            return;
        }

        dbContext.PipelineEvidence.Add(PipelineEvidence.FromBranchReady(runId, branchName));
    }

    /// <summary>
    /// Flushes any buffered output to the database.
    /// </summary>
    public async Task FlushAsync()
    {
        await FlushBufferAsync();
    }

    private void AppendLine(string line)
    {
        if (currentLog is null)
        {
            currentLog = new PipelineStageLog { RunId = runId, StageName = "pipeline", Status = "Running" };
            dbContext.PipelineStageLogs.Add(currentLog);
        }

        currentLog.Output = string.Concat(currentLog.Output, line, Environment.NewLine);
        dbContext.SaveChanges();
    }

    private async Task FlushBufferAsync()
    {
        if (buffer.Length == 0)
        {
            return;
        }

        if (currentLog is null)
        {
            currentLog = new PipelineStageLog { RunId = runId, StageName = "pipeline", Status = "Running" };
            dbContext.PipelineStageLogs.Add(currentLog);
        }

        currentLog.Output = string.Concat(currentLog.Output, buffer.ToString());
        buffer.Clear();
        await dbContext.SaveChangesAsync();
    }
}
