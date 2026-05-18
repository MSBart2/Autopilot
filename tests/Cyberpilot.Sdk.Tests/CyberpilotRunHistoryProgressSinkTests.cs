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
    public void OnBranchReady_PersistsBranchEvidence()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);

        sink.OnBranchReady("cyberpilot/issue-1-test");
        sink.OnBranchReady("cyberpilot/issue-1-test");

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("branch-reference", evidence.Kind);
        Assert.Equal("cyberpilot/issue-1-test", evidence.Name);
        Assert.Equal("git", evidence.Source);
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
    public void OnStageCompleted_WithStructuredEvidence_PersistsEvidenceLedgerRows()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);
        var stage = new StageDefinition("REVIEW", "review", "review.agent.md", "sdk/review");
        sink.OnStageStarted(stage, 1);
        var result = new StageResult(
            "STOP",
            "changes_requested",
            true,
            null,
            Artifacts: [new StageArtifact("pull-request", "PR #1", "https://github.com/owner/repo/pull/1")],
            Evidence: [new StageEvidence("test-output", "Tests failed.")],
            PolicyRationale: "Policy requires passing tests.",
            RequiredActions: ["Fix failing tests."]);

        sink.OnStageCompleted(stage, result);

        var rows = _dbContext.PipelineEvidence.OrderBy(row => row.Id).ToArray();
        Assert.Equal(4, rows.Length);
        Assert.All(rows, row => Assert.Equal(_run.Id, row.RunId));
        Assert.All(rows, row => Assert.Equal("review", row.StageName));
        Assert.All(rows, row => Assert.NotNull(row.StageLogId));
        Assert.Contains(rows, row => row.Kind == "stage-evidence" && row.Name == "test-output");
        Assert.Contains(rows, row => row.Kind == "stage-artifact" && row.Name == "pull-request");
        Assert.Contains(rows, row => row.Kind == "policy-rationale" && row.Summary == "Policy requires passing tests.");
        Assert.Contains(rows, row => row.Kind == "required-action" && row.Summary == "Fix failing tests.");
    }

    [Fact]
    public void OnStageCompleted_WithTokens_PersistsUsageMetricsEvidence()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "gpt-4.1", _dbContext);
        var stage = new StageDefinition("PLAN", "plan", "plan.agent.md", "sdk/plan");
        sink.OnStageStarted(stage, 1);

        sink.OnStageCompleted(stage, new StageResult("GO", "approved", true, null, InputTokens: 2_000_000, OutputTokens: 1_000_000));

        var evidence = _dbContext.PipelineEvidence.Single(row => row.Kind == "usage-metrics");
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("plan", evidence.StageName);
        Assert.Equal("usage", evidence.Name);
        Assert.Equal("telemetry", evidence.Source);
        Assert.Contains("2,000,000 input tokens", evidence.Summary);
        Assert.Contains("1,000,000 output tokens", evidence.Summary);
        Assert.Contains("$12.0000", evidence.Summary);
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
    public void OnStageCompleted_WithExecutionMetrics_PersistsMetricColumns()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "claude-sonnet-4.6", _dbContext);
        var stage = new StageDefinition("REVIEW", "review", "pipeline-review.agent.md", "sdk/reviewing");
        sink.OnStageStarted(stage, 1);

        var result = new StageResult(
            "GO",
            "approved",
            true,
            null,
            InputTokens: 1_000,
            OutputTokens: 500,
            Metrics: new StageExecutionMetrics(
                Model: "actual-model",
                InputTokens: 1_000,
                OutputTokens: 500,
                CacheReadTokens: 100,
                CacheWriteTokens: 25,
                ReasoningTokens: 10,
                PremiumRequestCost: 2,
                DurationMs: 1500,
                TurnCount: 3,
                ToolCallCount: 4,
                FailedToolCallCount: 1,
                SessionErrorCount: 1,
                ReachedIdle: true,
                WasAborted: false,
                ProviderCallIds: ["provider-1"],
                ApiCallIds: ["api-1"]));
        sink.OnStageCompleted(stage, result);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("actual-model", log.Model);
        Assert.Equal(100, log.CacheReadTokens);
        Assert.Equal(25, log.CacheWriteTokens);
        Assert.Equal(10, log.ReasoningTokens);
        Assert.Equal(2, log.PremiumRequestCost);
        Assert.Equal(1500, log.DurationMs);
        Assert.Equal(3, log.TurnCount);
        Assert.Equal(4, log.ToolCallCount);
        Assert.Equal(1, log.FailedToolCallCount);
        Assert.Equal(1, log.SessionErrorCount);
        Assert.True(log.ReachedIdle);
        Assert.False(log.WasAborted);
        Assert.Equal("provider-1", log.ProviderCallIds);
        Assert.Equal("api-1", log.ApiCallIds);
    }

    [Fact]
    public void OnStageCompleted_WithModelSelection_PersistsModelSelectionColumns()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "claude-sonnet-4.6", _dbContext);
        var stage = new StageDefinition("REVIEW", "review", "pipeline-review.agent.md", "sdk/reviewing");
        sink.OnStageStarted(stage, 1);

        var result = new StageResult(
            "GO",
            "approved",
            true,
            null,
            ConfiguredModel: "gpt-4.1",
            SelectedModel: "claude-haiku-4.5",
            FallbackModel: "claude-haiku-4.5",
            FallbackReason: "gpt-4.1 unavailable");
        sink.OnStageCompleted(stage, result);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("gpt-4.1", log.ConfiguredModel);
        Assert.Equal("claude-haiku-4.5", log.SelectedModel);
        Assert.Equal("claude-haiku-4.5", log.FallbackModel);
        Assert.Equal("gpt-4.1 unavailable", log.FallbackReason);
    }

    [Fact]
    public void OnStageCompleted_WithArtifacts_PersistsArtifactRows()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "claude-sonnet-4.6", _dbContext);
        var stage = new StageDefinition("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing");
        sink.OnStageStarted(stage, 1);

        var result = new StageResult(
            "GO",
            "approved",
            true,
            null,
            Artifacts: [new StageArtifact("pull-request", "PR #1 ready.", "https://github.com/owner/repo/pull/1", "text/markdown")]);
        sink.OnStageCompleted(stage, result);

        var artifact = _dbContext.PipelineArtifacts.Single();
        Assert.Equal(_run.Id, artifact.RunId);
        Assert.Equal("implement", artifact.StageName);
        Assert.Equal("pull-request", artifact.Name);
        Assert.Equal("PR #1 ready.", artifact.Value);
        Assert.Equal("https://github.com/owner/repo/pull/1", artifact.Uri);
        Assert.Equal("text/markdown", artifact.MediaType);
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

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("plan", evidence.StageName);
        Assert.Equal("approval-request", evidence.Kind);
        Assert.Equal("approval-history-1", evidence.Name);
        Assert.Contains("Plan approval required", evidence.Summary);
    }

    [Fact]
    public void OnDispatch_WithDeliveryOutcome_PersistsDeliveryEvidence()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);

        sink.OnDispatch(DispatchType.Routing, "Delivery complete — PR merged, branch cleaned up, landing report posted");
        sink.OnDispatch(DispatchType.Routing, "Review approved — dispatching to Docs stage");

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("deliver", evidence.StageName);
        Assert.Equal("delivery-outcome", evidence.Kind);
        Assert.Equal("delivery-complete", evidence.Name);
        Assert.Equal("dispatch", evidence.Source);
        Assert.Equal(2, _dbContext.PipelineDispatches.Count());
    }

    [Fact]
    public void OnDispatch_WithGateOutcome_PersistsGateEvidence()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);

        sink.OnDispatch(DispatchType.Gate, "Gate 'policy-ready' failed for stage 'triage': Policy review is incomplete.");

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("triage", evidence.StageName);
        Assert.Equal("gate-outcome", evidence.Kind);
        Assert.Equal("gate:policy-ready", evidence.Name);
        Assert.Equal("gate", evidence.Source);
        Assert.Single(_dbContext.PipelineDispatches);
    }

    [Fact]
    public void OnDispatch_WithRepositoryProfile_PersistsRepositoryProfileEvidence()
    {
        var sink = new CyberpilotRunHistoryProgressSink(_run.Id, "", _dbContext);

        sink.OnDispatch(DispatchType.RepositoryProfile, "Repository profile detected: languages: .NET | build: dotnet build ./App.sln.");

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("preflight", evidence.StageName);
        Assert.Equal("repository-profile", evidence.Kind);
        Assert.Equal("target-repository", evidence.Name);
        Assert.Equal("profile", evidence.Source);
        Assert.Single(_dbContext.PipelineDispatches);
    }
}
