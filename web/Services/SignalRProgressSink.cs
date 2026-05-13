using System.Text;
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
    ILogger logger) : ICyberpilotProgressSink
{
    private readonly StringBuilder buffer = new();
    private PipelineStageLog? currentLog;

    /// <inheritdoc />
    public void OnStageStarted(StageDefinition stage, int issueNumber)
    {
        FlushBufferAsync().GetAwaiter().GetResult();
        currentLog = new PipelineStageLog
        {
            RunId = runId,
            StageName = stage.Name,
            Status = "Running",
            StartedAt = DateTime.UtcNow,
        };
        dbContext.PipelineStageLogs.Add(currentLog);

        var run = dbContext.PipelineRuns.Find(runId);
        if (run is not null)
        {
            run.CurrentStage = stage.Name;
            if (run.Status != "Pausing")
                run.Status = "Running";
        }

        dbContext.SaveChanges();
        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("stageStarted", new { runId, issueNumber, stage = stage.Name }).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnStageCompleted(StageDefinition stage, StageResult result)
    {
        FlushBufferAsync().GetAwaiter().GetResult();
        if (currentLog is not null)
        {
            currentLog.Status = result.Status;
            currentLog.CompletedAt = DateTime.UtcNow;
            currentLog.InputTokens = result.InputTokens;
            currentLog.OutputTokens = result.OutputTokens;
            currentLog.EstimatedCostUsd = ModelPricingService.Estimate(model, result.InputTokens, result.OutputTokens);
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
            estimatedCostUsd = ModelPricingService.Estimate(model, result.InputTokens, result.OutputTokens),
        }).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnBranchReady(string branchName)
    {
        var run = dbContext.PipelineRuns.Find(runId);
        if (run is not null)
        {
            run.BranchName = branchName;
            dbContext.SaveChanges();
        }

        hubContext.Clients.Group(PipelineHub.GroupName(runId)).SendAsync("branchReady", new { runId, branchName }).GetAwaiter().GetResult();
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
