using System.Text;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists Cyberpilot progress events into the shared run-history database.
/// </summary>
public sealed class CyberpilotRunHistoryProgressSink(string runId, CyberpilotDbContext dbContext) : ICyberpilotProgressSink
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
            run.Status = "Running";
        }

        dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void OnStageCompleted(StageDefinition stage, StageResult result)
    {
        FlushBufferAsync().GetAwaiter().GetResult();
        if (currentLog is not null)
        {
            currentLog.Status = result.Status;
            currentLog.CompletedAt = DateTime.UtcNow;
        }

        dbContext.SaveChanges();
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
    }

    /// <inheritdoc />
    public void OnMessage(string level, string message)
    {
        AppendLine($"[{level}] {message}");
    }

    /// <inheritdoc />
    public void OnStreamDelta(string content)
    {
        buffer.Append(content);
        if (buffer.Length > 4096 || content.Contains('\n', StringComparison.Ordinal))
        {
            FlushBufferAsync().GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public void OnDispatch(string type, string message)
    {
        dbContext.PipelineDispatches.Add(new PipelineDispatch
        {
            RunId = runId,
            Type = type,
            Message = message,
        });
        dbContext.SaveChanges();
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
