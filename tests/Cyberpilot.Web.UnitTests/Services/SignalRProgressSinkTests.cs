using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Hubs;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cyberpilot.Web.UnitTests.Services;

public class SignalRProgressSinkTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CyberpilotDbContext _dbContext;
    private readonly Mock<IHubContext<PipelineHub>> _hubContext;
    private readonly Mock<IClientProxy> _groupClient;
    private readonly PipelineRun _run;

    public SignalRProgressSinkTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CyberpilotDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new CyberpilotDbContext(options);
        _dbContext.Database.EnsureCreated();

        _run = new PipelineRun { IssueNumber = 42, Repository = "test/repo", Model = "m", Status = "Queued" };
        _dbContext.PipelineRuns.Add(_run);
        _dbContext.SaveChanges();

        // Set up mock hub context
        _hubContext = new Mock<IHubContext<PipelineHub>>();
        var mockClients = new Mock<IHubClients>();
        _groupClient = new Mock<IClientProxy>();
        _hubContext.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_groupClient.Object);
        _groupClient
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void OnStageStarted_CreatesStageLogAndUpdatesRun()
    {
        var sink = CreateSink();
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");

        sink.OnStageStarted(stage, 42);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("triage", log.StageName);
        Assert.Equal("Running", log.Status);

        var updatedRun = _dbContext.PipelineRuns.Find(_run.Id)!;
        Assert.Equal("triage", updatedRun.CurrentStage);
    }

    [Fact]
    public void OnStageStarted_ForReviewDimension_DoesNotReplaceParentCurrentStage()
    {
        var sink = CreateSink();
        var review = new StageDefinition("REVIEW", "review", "review.agent.md", "sdk/review");
        var dimension = new StageDefinition("REVIEW/SECURITY", "review:security", "security-reviewer.agent.md", "sdk/review");

        sink.OnStageStarted(review, 42);
        sink.OnStageStarted(dimension, 42);

        var updatedRun = _dbContext.PipelineRuns.Find(_run.Id)!;
        Assert.Equal("review", updatedRun.CurrentStage);
        var stageNames = _dbContext.PipelineStageLogs.OrderBy(log => log.Id).Select(log => log.StageName).ToArray();
        Assert.Equal(new[] { "review", "review:security" }, stageNames);
    }

    [Fact]
    public void OnBranchReady_PersistsBranchEvidence()
    {
        var sink = CreateSink();

        sink.OnBranchReady("cyberpilot/issue-42-test");
        sink.OnBranchReady("cyberpilot/issue-42-test");

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("branch-reference", evidence.Kind);
        Assert.Equal("cyberpilot/issue-42-test", evidence.Name);
    }

    [Fact]
    public void OnStageCompleted_SetsStatusAndCompletedAt()
    {
        var sink = CreateSink();
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
        sink.OnStageStarted(stage, 42);

        sink.OnStageCompleted(stage, new StageResult("GO", "approved", true, null));

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
        var sink = CreateSink();
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
        sink.OnStageStarted(stage, 42);

        sink.OnStageCompleted(stage, StageResult.Empty with { ContractVersion = "1.1" });

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("1.1", log.StageResultContractVersion);
        Assert.Contains("\"ContractVersion\":\"1.1\"", log.StageResultJson);
    }

    [Fact]
    public void OnStageCompleted_WithStructuredEvidence_PersistsEvidenceLedgerRows()
    {
        var sink = CreateSink();
        var stage = new StageDefinition("REVIEW", "review", "review.agent.md", "sdk/review");
        sink.OnStageStarted(stage, 42);
        var result = new StageResult(
            "STOP",
            "changes_requested",
            true,
            null,
            Evidence: [new StageEvidence("review-verdict", "Review requested changes.")],
            RequiredActions: ["Return to implementation."]);

        sink.OnStageCompleted(stage, result);

        var rows = _dbContext.PipelineEvidence.OrderBy(row => row.Id).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row.Kind == "stage-evidence" && row.Name == "review-verdict");
        Assert.Contains(rows, row => row.Kind == "required-action" && row.Summary == "Return to implementation.");
        Assert.All(rows, row => Assert.Equal(_run.Id, row.RunId));
        Assert.All(rows, row => Assert.Equal("review", row.StageName));
    }

    [Fact]
    public void OnMessage_AppendsLineToLog()
    {
        var sink = CreateSink();
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
        sink.OnStageStarted(stage, 42);

        sink.OnMessage("info", "Test message");

        var log = _dbContext.PipelineStageLogs.First();
        Assert.Contains("[info] Test message", log.Output);
    }

    [Fact]
    public void OnStreamDelta_BuffersAndFlushesOnNewline()
    {
        var sink = CreateSink();
        var stage = new StageDefinition("PLAN", "plan", "plan.agent.md", "sdk/planning");
        sink.OnStageStarted(stage, 42);

        sink.OnStreamDelta("part1");
        sink.OnStreamDelta("part2\n");

        var log = _dbContext.PipelineStageLogs.First();
        Assert.Contains("part1part2", log.Output);
    }

    [Fact]
    public void OnMessage_WithoutStageStarted_CreatesGenericLog()
    {
        var sink = CreateSink();
        sink.OnMessage("warn", "No stage yet");

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("pipeline", log.StageName);
    }

    [Fact]
    public void OnStageCompleted_WithTokens_WritesToStageLog()
    {
        var sink = CreateSink("gpt-4.1");
        var stage = new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
        sink.OnStageStarted(stage, 42);

        var result = new StageResult("GO", "approved", true, null, InputTokens: 2_000_000, OutputTokens: 1_000_000);
        sink.OnStageCompleted(stage, result);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal(2_000_000, log.InputTokens);
        Assert.Equal(1_000_000, log.OutputTokens);
        Assert.Equal(12.0m, log.EstimatedCostUsd); // 4.00 in + 8.00 out = $12.00

        var evidence = _dbContext.PipelineEvidence.Single(row => row.Kind == "usage-metrics");
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("triage", evidence.StageName);
        Assert.Contains("2,000,000 input tokens", evidence.Summary);
        Assert.Contains("1,000,000 output tokens", evidence.Summary);
        Assert.Contains("$12.0000", evidence.Summary);
    }

    [Fact]
    public void OnStageCompleted_WithExecutionMetrics_WritesToStageLog()
    {
        var sink = CreateSink("gpt-4.1");
        var stage = new StageDefinition("REVIEW", "review", "pipeline-review.agent.md", "sdk/reviewing");
        sink.OnStageStarted(stage, 42);

        var result = new StageResult(
            "GO",
            "approved",
            true,
            null,
            InputTokens: 2_000,
            OutputTokens: 1_000,
            Metrics: new StageExecutionMetrics(
                Model: "actual-model",
                TurnCount: 2,
                ToolCallCount: 5,
                FailedToolCallCount: 1,
                SessionErrorCount: 1,
                DurationMs: 1200,
                ProviderCallIds: ["provider-1"],
                ApiCallIds: ["api-1"]));
        sink.OnStageCompleted(stage, result);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("actual-model", log.Model);
        Assert.Equal(2, log.TurnCount);
        Assert.Equal(5, log.ToolCallCount);
        Assert.Equal(1, log.FailedToolCallCount);
        Assert.Equal(1, log.SessionErrorCount);
        Assert.Equal(1200, log.DurationMs);
        Assert.Equal("provider-1", log.ProviderCallIds);
        Assert.Equal("api-1", log.ApiCallIds);
    }

    [Fact]
    public void OnStageCompleted_WithModelSelection_WritesModelSelectionColumns()
    {
        var sink = CreateSink("claude-sonnet-4.6");
        var stage = new StageDefinition("REVIEW", "review", "pipeline-review.agent.md", "sdk/reviewing");
        sink.OnStageStarted(stage, 42);

        var result = new StageResult(
            "GO",
            "approved",
            true,
            null,
            InputTokens: 1_000_000,
            OutputTokens: 500_000,
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
        Assert.Equal(2.8m, log.EstimatedCostUsd);
    }

    [Fact]
    public void OnStageCompleted_WithSdkSessionId_WritesResumeMetadata()
    {
        var sink = CreateSink("claude-sonnet-4.6");
        var stage = new StageDefinition("PLAN", "plan", "plan.agent.md", "sdk/planning");
        sink.OnStageStarted(stage, 42);

        var result = StageResult.Empty with
        {
            SdkSessionId = "cyberpilot-run-plan-0",
            Metrics = new StageExecutionMetrics(ReachedIdle: true, WasAborted: false),
        };
        sink.OnStageCompleted(stage, result);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("cyberpilot-run-plan-0", log.SdkSessionId);
        Assert.Equal("completed", log.SessionState);
        Assert.Equal("not_applicable", log.ResumeEligibility);
        Assert.Contains("completed successfully", log.ResumeBlockedReason);
        Assert.NotNull(log.SessionCleanupAfter);
    }

    [Fact]
    public void OnStageCompleted_WithArtifacts_WritesArtifactRows()
    {
        var sink = CreateSink("gpt-4.1");
        var stage = new StageDefinition("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing");
        sink.OnStageStarted(stage, 42);

        var result = new StageResult(
            "GO",
            "approved",
            true,
            null,
            Artifacts: [new StageArtifact("validation-summary", "dotnet test passed.", "log://validation", "text/plain")]);
        sink.OnStageCompleted(stage, result);

        var artifact = _dbContext.PipelineArtifacts.Single();
        Assert.Equal(_run.Id, artifact.RunId);
        Assert.Equal("implement", artifact.StageName);
        Assert.Equal("validation-summary", artifact.Name);
        Assert.Equal("dotnet test passed.", artifact.Value);
        Assert.Equal("log://validation", artifact.Uri);
        Assert.Equal("text/plain", artifact.MediaType);
    }

    [Fact]
    public void OnStageStarted_WithRetryReason_WritesReasonToMatchingStageLog()
    {
        var sink = new SignalRProgressSink(
            _run.Id,
            string.Empty,
            42,
            _dbContext,
            _hubContext.Object,
            NullLogger.Instance,
            retryStageName: "implement",
            retryReason: "Need to address review findings.");
        var stage = new StageDefinition("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing");

        sink.OnStageStarted(stage, 42);

        var log = _dbContext.PipelineStageLogs.Single();
        Assert.Equal("Need to address review findings.", log.RetryReason);

        _groupClient.Verify(client => client.SendCoreAsync(
            "stageStarted",
            It.Is<object?[]>(arguments =>
                arguments.Length == 1
                && (string?)arguments[0]!.GetType().GetProperty("retryReason")!.GetValue(arguments[0]) == "Need to address review findings."),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void OnApprovalRequested_PersistsApprovalAndNotifiesClients()
    {
        var sink = CreateSink();
        var request = new ApprovalGateRequest(
            "approval-signalr-1",
            42,
            "review",
            GateTiming.AfterStage,
            "Review approval required before delivery.",
            "maintainer",
            "docs",
            DateTimeOffset.Parse("2026-05-13T10:00:00Z"));

        sink.OnApprovalRequested(request);

        var approval = _dbContext.PipelineApprovals.Single();
        Assert.Equal(_run.Id, approval.RunId);
        Assert.Equal("approval-signalr-1", approval.Id);
        Assert.Equal("review", approval.StageName);
        Assert.Equal("Pending", approval.Status);

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("review", evidence.StageName);
        Assert.Equal("approval-request", evidence.Kind);
        Assert.Equal("approval-signalr-1", evidence.Name);

        _groupClient.Verify(client => client.SendCoreAsync(
            "approvalRequested",
            It.Is<object?[]>(arguments =>
                arguments.Length == 1
                && (string?)arguments[0]!.GetType().GetProperty("approvalId")!.GetValue(arguments[0]) == "approval-signalr-1"),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void OnDispatch_WithSkipDeliver_PersistsDeliveryEvidenceAndNotifiesClients()
    {
        var sink = CreateSink();

        sink.OnDispatch(DispatchType.Skip, "Skip-deliver enabled — pipeline complete, PR ready for manual merge");

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("deliver", evidence.StageName);
        Assert.Equal("delivery-outcome", evidence.Kind);
        Assert.Equal("delivery-skipped", evidence.Name);

        _groupClient.Verify(client => client.SendCoreAsync(
            "cyberpilotDispatch",
            It.Is<object?[]>(arguments =>
                arguments.Length == 1
                && (string?)arguments[0]!.GetType().GetProperty("type")!.GetValue(arguments[0]) == DispatchType.Skip),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void OnDispatch_WithGateOutcome_PersistsGateEvidenceAndNotifiesClients()
    {
        var sink = CreateSink();

        sink.OnDispatch(DispatchType.Gate, "Gate 'review-approved' passed for stage 'review': Review approved the pull request.");

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("review", evidence.StageName);
        Assert.Equal("gate-outcome", evidence.Kind);
        Assert.Equal("gate:review-approved", evidence.Name);
        Assert.Equal("gate", evidence.Source);

        _groupClient.Verify(client => client.SendCoreAsync(
            "cyberpilotDispatch",
            It.Is<object?[]>(arguments =>
                arguments.Length == 1
                && (string?)arguments[0]!.GetType().GetProperty("type")!.GetValue(arguments[0]) == DispatchType.Gate),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void OnDispatch_WithRepositoryProfile_PersistsRepositoryProfileEvidenceAndNotifiesClients()
    {
        var sink = CreateSink();

        sink.OnDispatch(DispatchType.RepositoryProfile, "Repository profile detected: languages: .NET | build: dotnet build ./App.sln.");

        var evidence = _dbContext.PipelineEvidence.Single();
        Assert.Equal(_run.Id, evidence.RunId);
        Assert.Equal("preflight", evidence.StageName);
        Assert.Equal("repository-profile", evidence.Kind);
        Assert.Equal("target-repository", evidence.Name);
        Assert.Equal("profile", evidence.Source);

        _groupClient.Verify(client => client.SendCoreAsync(
            "cyberpilotDispatch",
            It.Is<object?[]>(arguments =>
                arguments.Length == 1
                && (string?)arguments[0]!.GetType().GetProperty("type")!.GetValue(arguments[0]) == DispatchType.RepositoryProfile),
            It.IsAny<CancellationToken>()));
    }

    private SignalRProgressSink CreateSink(string model = "")
    {
        return new SignalRProgressSink(
            _run.Id,
            model,
            42,
            _dbContext,
            _hubContext.Object,
            NullLogger.Instance);
    }
}
