using System.Text;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists Cyberpilot progress events into the shared run-history database.
/// </summary>
public sealed class CyberpilotRunHistoryProgressSink(string runId, string model, CyberpilotDbContext dbContext) : ICyberpilotProgressSink
{
    private readonly StringBuilder buffer = new();
    private PipelineStageLog? currentLog;

    /// <inheritdoc />
    public void OnStageStarted(StageDefinition stage, int issueNumber)
    {
        FlushBuffer();
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
        dbContext.PipelineStageLogs.Add(currentLog);

        var run = dbContext.PipelineRuns.Find(runId);
        if (run is not null)
        {
            if (!IsReviewDimensionStage(stage))
            {
                run.CurrentStage = stage.Name;
            }

            run.Status = "Running";
        }

        dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void OnStageCompleted(StageDefinition stage, StageResult result)
    {
        FlushBuffer();
        var stageLog = ResolveStageLog(stage);
        if (stageLog is not null)
        {
            PipelineStageLogResultMapper.Apply(stageLog, result, model, DateTime.UtcNow);
            dbContext.PipelineArtifacts.AddRange(PipelineArtifact.FromStageResult(runId, stage.Name, stageLog, result));
            dbContext.PipelineEvidence.AddRange(PipelineEvidence.FromStageResult(runId, stage.Name, stageLog, result));
            dbContext.PipelineToolFailures.AddRange(PipelineToolFailure.FromStageResult(runId, stage.Name, stageLog, result));
        }

        dbContext.SaveChanges();
    }

    private PipelineStageLog? ResolveStageLog(StageDefinition stage)
    {
        if (currentLog is not null
            && currentLog.RunId.Equals(runId, StringComparison.OrdinalIgnoreCase)
            && currentLog.StageName.Equals(stage.Name, StringComparison.OrdinalIgnoreCase)
            && currentLog.CompletedAt is null)
        {
            return currentLog;
        }

        return dbContext.PipelineStageLogs
            .Where(log => log.RunId == runId
                && log.StageName == stage.Name
                && log.CompletedAt == null)
            .OrderByDescending(log => log.StartedAt)
            .FirstOrDefault();
    }

    private static bool IsReviewDimensionStage(StageDefinition stage)
        => stage.Name.StartsWith("review:", StringComparison.OrdinalIgnoreCase);

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
            FlushBuffer();
        }
    }

    /// <inheritdoc />
    public void OnDispatch(string type, string message)
    {
        AddDeliveryEvidence(type, message);
        AddGateEvidence(type, message);
        AddRepositoryProfileEvidence(type, message);
        dbContext.PipelineDispatches.Add(new PipelineDispatch
        {
            RunId = runId,
            Type = type,
            Message = message,
        });
        dbContext.SaveChanges();
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

    private void FlushBuffer()
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
        dbContext.SaveChanges();
    }
}
