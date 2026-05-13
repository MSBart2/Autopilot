using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cyberpilot.Sdk.Tests;

public sealed class CyberpilotRunHistoryProgressSinkTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CyberpilotDbContext _dbContext;
    private readonly PipelineRun _run;

    public CyberpilotRunHistoryProgressSinkTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CyberpilotDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new CyberpilotDbContext(options);
        _dbContext.Database.EnsureCreated();

        _run = new PipelineRun { IssueNumber = 1, Repository = "test/repo", Model = "m", Status = "Queued" };
        _dbContext.PipelineRuns.Add(_run);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void OnStageStarted_CreatesStageLogAndUpdatesRun()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");

        sink.OnStageStarted(stage, 1);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("triage", log.StageName);
        Assert.Equal("Running", log.Status);
        Assert.Equal(_run.Id, log.RunId);

        var updatedRun = _dbContext.PipelineRuns.Find(_run.Id)!;
        Assert.Equal("triage", updatedRun.CurrentStage);
        Assert.Equal("Running", updatedRun.Status);
    }

    [Fact]
    public void OnStageCompleted_UpdatesStageLogStatus()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
        sink.OnStageStarted(stage, 1);

        var result = new StageResult("GO", "approved", true, null);
        sink.OnStageCompleted(stage, result);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("GO", log.Status);
        Assert.NotNull(log.CompletedAt);
        Assert.Contains("\"Status\":\"GO\"", log.StageResultJson);
        Assert.Contains("\"Decision\":\"approved\"", log.StageResultJson);
        Assert.Equal(PipelineDefinitionDefaults.ContractVersion, log.StageResultContractVersion);
    }

    [Fact]
    public void OnStageCompleted_WithResultContractVersion_PersistsResultContractVersion()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
        sink.OnStageStarted(stage, 1);

        sink.OnStageCompleted(stage, StageResult.Empty with { ContractVersion = "1.1" });

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("1.1", log.StageResultContractVersion);
        Assert.Contains("\"ContractVersion\":\"1.1\"", log.StageResultJson);
    }

    [Fact]
    public void OnMessage_AppendsToStageLog()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
        sink.OnStageStarted(stage, 1);

        sink.OnMessage("info", "Hello world");

        var log = _dbContext.PipelineStageLogs.First();
        Assert.Contains("[info] Hello world", log.Output);
    }

    [Fact]
    public void OnMessage_WithoutStageStarted_CreatesGenericLog()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        sink.OnMessage("warn", "No stage");

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("pipeline", log.StageName);
        Assert.Contains("[warn] No stage", log.Output);
    }

    [Fact]
    public void OnStreamDelta_BuffersContent()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        var stage = new StageDefinition("PLAN", "plan", "plan.agent.md", "sdk/planning");
        sink.OnStageStarted(stage, 1);

        sink.OnStreamDelta("chunk1");
        sink.OnStreamDelta("chunk2\n");

        var log = _dbContext.PipelineStageLogs.First();
        Assert.Contains("chunk1chunk2", log.Output);
    }

    [Fact]
    public void OnStreamDelta_FlushesWhenBufferExceeds4096()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        var stage = new StageDefinition("PLAN", "plan", "plan.agent.md", "sdk/planning");
        sink.OnStageStarted(stage, 1);

        var largeContent = new string('x', 4097);
        sink.OnStreamDelta(largeContent);

        var log = _dbContext.PipelineStageLogs.First();
        Assert.Contains(largeContent, log.Output);
    }

    [Fact]
    public void OnStageCompleted_WithTokenUsage_PersistsTokensAndCost()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "claude-sonnet-4.6", _dbContext);
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
        sink.OnStageStarted(stage, 1);

        var result = new StageResult("GO", "approved", true, null, InputTokens: 1_000_000, OutputTokens: 500_000);
        sink.OnStageCompleted(stage, result);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal(1_000_000, log.InputTokens);
        Assert.Equal(500_000, log.OutputTokens);
        Assert.Equal(10.5m, log.EstimatedCostUsd); // 3.00 in + 7.50 out = $10.50
    }

    [Fact]
    public void OnApprovalRequested_PersistsApprovalRequest()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        var request = new ApprovalGateRequest(
            "approval-history-1",
            _run.IssueNumber,
            "plan",
            GateTiming.AfterStage,
            "Plan approval required before implementation.",
            "maintainer",
            "implement",
            DateTimeOffset.Parse("2026-05-13T10:00:00Z"));

        sink.OnApprovalRequested(request);

        var approval = _dbContext.PipelineApprovals.Single();
        Assert.Equal(_run.Id, approval.RunId);
        Assert.Equal("approval-history-1", approval.Id);
        Assert.Equal("Pending", approval.Status);
        Assert.Equal("maintainer", approval.RequestedRole);
        Assert.Equal("implement", approval.ResumeStageName);
    }
}
