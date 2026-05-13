using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cyberpilot.Sdk.Tests;

public sealed class CyberpilotDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CyberpilotDbContext _dbContext;

    public CyberpilotDbContextTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CyberpilotDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new CyberpilotDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void EnsureCreated_CreatesTables()
    {
        Assert.True(_dbContext.Database.CanConnect());
    }

    [Fact]
    public async Task PipelineRun_CanBeAddedAndRetrieved()
    {
        var run = new PipelineRun { IssueNumber = 42, Repository = "test/repo", Model = "test-model" };
        _dbContext.PipelineRuns.Add(run);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.PipelineRuns.FindAsync(run.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(42, retrieved.IssueNumber);
        Assert.Equal("Queued", retrieved.Status);
    }

    [Fact]
    public async Task PipelineRun_PipelineDefinitionMetadata_CanBeAddedAndRetrieved()
    {
        var run = new PipelineRun
        {
            IssueNumber = 42,
            Repository = "test/repo",
            Model = "test-model",
            PipelineDefinitionName = PipelineDefinitionDefaults.DefinitionName,
            PipelineDefinitionVersion = PipelineDefinitionDefaults.DefinitionVersion,
            PolicyProfileName = PipelineDefinitionDefaults.PolicyProfileName,
            ContractVersion = PipelineDefinitionDefaults.ContractVersion,
        };
        _dbContext.PipelineRuns.Add(run);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.PipelineRuns.FindAsync(run.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(PipelineDefinitionDefaults.DefinitionName, retrieved.PipelineDefinitionName);
        Assert.Equal(PipelineDefinitionDefaults.DefinitionVersion, retrieved.PipelineDefinitionVersion);
        Assert.Equal(PipelineDefinitionDefaults.PolicyProfileName, retrieved.PolicyProfileName);
        Assert.Equal(PipelineDefinitionDefaults.ContractVersion, retrieved.ContractVersion);
    }

    [Fact]
    public void PipelineRun_DefaultValues_AreCorrect()
    {
        var run = new PipelineRun();
        Assert.Equal("Queued", run.Status);
        Assert.NotNull(run.Id);
        Assert.Equal(string.Empty, run.Repository);
    }

    [Fact]
    public void PipelineStageLog_DefaultValues_AreCorrect()
    {
        var log = new PipelineStageLog();
        Assert.Equal(string.Empty, log.RunId);
        Assert.Equal(string.Empty, log.StageName);
        Assert.Equal(string.Empty, log.Status);
        Assert.Null(log.Output);
    }

    [Fact]
    public void PipelineStageLog_TokenColumns_DefaultToNull()
    {
        var log = new PipelineStageLog();
        Assert.Null(log.InputTokens);
        Assert.Null(log.OutputTokens);
        Assert.Null(log.EstimatedCostUsd);
    }

    [Fact]
    public void PipelineStageLog_StructuredResultColumns_DefaultToNull()
    {
        var log = new PipelineStageLog();

        Assert.Null(log.StageResultJson);
        Assert.Null(log.StageResultContractVersion);
        Assert.Null(log.RetryReason);
    }

    [Fact]
    public async Task PipelineStageLog_StructuredResultMetadata_CanBeAddedAndRetrieved()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "test/repo", Model = "m" };
        _dbContext.PipelineRuns.Add(run);
        var log = new PipelineStageLog
        {
            RunId = run.Id,
            StageName = "review",
            Status = "GO",
            StageResultJson = "{\"status\":\"GO\",\"decision\":\"approved\"}",
            StageResultContractVersion = PipelineDefinitionDefaults.ContractVersion,
            RetryReason = "Retry after addressing review feedback.",
        };
        _dbContext.PipelineStageLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.PipelineStageLogs.FindAsync(log.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(log.StageResultJson, retrieved.StageResultJson);
        Assert.Equal(PipelineDefinitionDefaults.ContractVersion, retrieved.StageResultContractVersion);
        Assert.Equal("Retry after addressing review feedback.", retrieved.RetryReason);
    }

    [Fact]
    public async Task PipelineRun_CascadeDeletesStageLogs()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "test/repo", Model = "m" };
        _dbContext.PipelineRuns.Add(run);
        var log = new PipelineStageLog { RunId = run.Id, StageName = "triage", Status = "Running" };
        _dbContext.PipelineStageLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        _dbContext.PipelineRuns.Remove(run);
        await _dbContext.SaveChangesAsync();

        Assert.Empty(await _dbContext.PipelineStageLogs.ToArrayAsync());
    }

    [Fact]
    public async Task PipelineApproval_CanBeAddedAndRetrieved()
    {
        var run = new PipelineRun { IssueNumber = 42, Repository = "test/repo", Model = "m" };
        _dbContext.PipelineRuns.Add(run);
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "review",
            Timing = "AfterStage",
            Reason = "Maintainer approval required before delivery.",
            RequestedRole = "maintainer",
            ResumeStageName = "docs",
        };
        _dbContext.PipelineApprovals.Add(approval);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.PipelineApprovals.FindAsync(approval.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(run.Id, retrieved.RunId);
        Assert.Equal(42, retrieved.IssueNumber);
        Assert.Equal("review", retrieved.StageName);
        Assert.Equal("AfterStage", retrieved.Timing);
        Assert.Equal("Pending", retrieved.Status);
        Assert.Equal("maintainer", retrieved.RequestedRole);
        Assert.Equal("docs", retrieved.ResumeStageName);
    }

    [Fact]
    public async Task PipelineEvidence_CanBeAddedAndRetrieved()
    {
        var run = new PipelineRun { IssueNumber = 42, Repository = "test/repo", Model = "m" };
        _dbContext.PipelineRuns.Add(run);
        var log = new PipelineStageLog { RunId = run.Id, StageName = "review", Status = "GO" };
        _dbContext.PipelineStageLogs.Add(log);
        var evidence = new PipelineEvidence
        {
            RunId = run.Id,
            StageLog = log,
            StageName = "review",
            Kind = "stage-evidence",
            Name = "review-verdict",
            Summary = "Review approved the pull request.",
            Uri = "https://github.com/owner/repo/pull/1",
            MediaType = "text/markdown",
        };
        _dbContext.PipelineEvidence.Add(evidence);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.PipelineEvidence.FindAsync(evidence.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(run.Id, retrieved.RunId);
        Assert.Equal(log.Id, retrieved.StageLogId);
        Assert.Equal("review", retrieved.StageName);
        Assert.Equal("stage-evidence", retrieved.Kind);
        Assert.Equal("review-verdict", retrieved.Name);
        Assert.Equal("Review approved the pull request.", retrieved.Summary);
        Assert.Equal("https://github.com/owner/repo/pull/1", retrieved.Uri);
        Assert.Equal("text/markdown", retrieved.MediaType);
        Assert.Equal("stage-result", retrieved.Source);
    }

    [Fact]
    public async Task PipelineEvidence_CascadeDeletesWithRun()
    {
        var run = new PipelineRun { IssueNumber = 42, Repository = "test/repo", Model = "m" };
        _dbContext.PipelineRuns.Add(run);
        _dbContext.PipelineEvidence.Add(new PipelineEvidence
        {
            RunId = run.Id,
            StageName = "plan",
            Kind = "required-action",
            Name = "required-action-1",
            Summary = "Fix failing tests.",
        });
        await _dbContext.SaveChangesAsync();

        _dbContext.PipelineRuns.Remove(run);
        await _dbContext.SaveChangesAsync();

        Assert.Empty(await _dbContext.PipelineEvidence.ToArrayAsync());
    }

    [Fact]
    public void PipelineEvidence_FromStageResult_CreatesLedgerRows()
    {
        var result = new StageResult(
            "STOP",
            "changes_requested",
            true,
            null,
            Artifacts: [new StageArtifact("pull-request", "PR #1", "https://github.com/owner/repo/pull/1", "text/uri-list")],
            Evidence: [new StageEvidence("test-output", "Tests failed.", "file://test.log")],
            PolicyRationale: "Strict policy requires tests to pass.",
            RequiredActions: ["Fix failing tests."]);

        var rows = PipelineEvidence.FromStageResult("run-1", "review", null, result);

        Assert.Collection(
            rows,
            evidence =>
            {
                Assert.Equal("stage-evidence", evidence.Kind);
                Assert.Equal("test-output", evidence.Name);
                Assert.Equal("Tests failed.", evidence.Summary);
                Assert.Equal("file://test.log", evidence.Uri);
            },
            artifact =>
            {
                Assert.Equal("stage-artifact", artifact.Kind);
                Assert.Equal("pull-request", artifact.Name);
                Assert.Equal("PR #1", artifact.Summary);
                Assert.Equal("https://github.com/owner/repo/pull/1", artifact.Uri);
                Assert.Equal("text/uri-list", artifact.MediaType);
            },
            rationale =>
            {
                Assert.Equal("policy-rationale", rationale.Kind);
                Assert.Equal("Strict policy requires tests to pass.", rationale.Summary);
            },
            action =>
            {
                Assert.Equal("required-action", action.Kind);
                Assert.Equal("required-action-1", action.Name);
                Assert.Equal("Fix failing tests.", action.Summary);
            });
    }

    [Fact]
    public async Task PipelineApproval_FromPendingRequest_CanBeAddedAndRetrieved()
    {
        var run = new PipelineRun { IssueNumber = 42, Repository = "test/repo", Model = "m" };
        _dbContext.PipelineRuns.Add(run);
        var request = new ApprovalGateRequest(
            "approval-pending",
            run.IssueNumber,
            "plan",
            GateTiming.AfterStage,
            "Plan approval required before implementation.",
            "maintainer",
            "implement",
            DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
        _dbContext.PipelineApprovals.Add(PipelineApproval.FromRequest(run.Id, request));
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.PipelineApprovals.FindAsync("approval-pending");

        Assert.NotNull(retrieved);
        Assert.Equal(run.Id, retrieved.RunId);
        Assert.Equal("plan", retrieved.StageName);
        Assert.Equal("AfterStage", retrieved.Timing);
        Assert.Equal("Pending", retrieved.Status);
        Assert.Equal("Plan approval required before implementation.", retrieved.Reason);
        Assert.Equal("maintainer", retrieved.RequestedRole);
        Assert.Equal("implement", retrieved.ResumeStageName);
        Assert.Equal(DateTime.Parse("2026-05-13T10:00:00Z").ToUniversalTime(), retrieved.CreatedAt);
        Assert.Null(retrieved.DecidedBy);
        Assert.Null(retrieved.DecisionReason);
        Assert.Null(retrieved.DecidedAt);
    }

    [Theory]
    [InlineData("Approved", "ship it")]
    [InlineData("Rejected", "needs another look")]
    public async Task PipelineApproval_FromDecidedRequest_CanBeAddedAndRetrieved(string expectedStatus, string decisionReason)
    {
        var run = new PipelineRun { IssueNumber = 42, Repository = "test/repo", Model = "m" };
        _dbContext.PipelineRuns.Add(run);
        var request = new ApprovalGateRequest(
            $"approval-{expectedStatus.ToLowerInvariant()}",
            run.IssueNumber,
            "review",
            GateTiming.AfterStage,
            "Review approval required before delivery.",
            "maintainer",
            "docs",
            DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
        request = expectedStatus == "Approved"
            ? request.Approve("alice", decisionReason, DateTimeOffset.Parse("2026-05-13T10:15:00Z"))
            : request.Reject("alice", decisionReason, DateTimeOffset.Parse("2026-05-13T10:15:00Z"));
        _dbContext.PipelineApprovals.Add(PipelineApproval.FromRequest(run.Id, request));
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.PipelineApprovals.FindAsync(request.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(expectedStatus, retrieved.Status);
        Assert.Equal("alice", retrieved.DecidedBy);
        Assert.Equal(decisionReason, retrieved.DecisionReason);
        Assert.Equal(DateTime.Parse("2026-05-13T10:15:00Z").ToUniversalTime(), retrieved.DecidedAt);
    }

    [Fact]
    public async Task PipelineRun_CascadeDeletesApprovals()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "test/repo", Model = "m" };
        _dbContext.PipelineRuns.Add(run);
        _dbContext.PipelineApprovals.Add(new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "plan",
            Timing = "AfterStage",
            Reason = "Plan approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "implement",
        });
        await _dbContext.SaveChangesAsync();

        _dbContext.PipelineRuns.Remove(run);
        await _dbContext.SaveChangesAsync();

        Assert.Empty(await _dbContext.PipelineApprovals.ToArrayAsync());
    }

    [Fact]
    public async Task PipelineRun_IssueNumberIndex_AllowsDuplicates()
    {
        _dbContext.PipelineRuns.Add(new PipelineRun { IssueNumber = 99, Repository = "r", Model = "m" });
        _dbContext.PipelineRuns.Add(new PipelineRun { IssueNumber = 99, Repository = "r", Model = "m" });
        await _dbContext.SaveChangesAsync();
        Assert.Equal(2, await _dbContext.PipelineRuns.CountAsync(r => r.IssueNumber == 99));
    }
}
