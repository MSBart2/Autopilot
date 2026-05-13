using Cyberpilot.Persistence;
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
    public async Task PipelineRun_IssueNumberIndex_AllowsDuplicates()
    {
        _dbContext.PipelineRuns.Add(new PipelineRun { IssueNumber = 99, Repository = "r", Model = "m" });
        _dbContext.PipelineRuns.Add(new PipelineRun { IssueNumber = 99, Repository = "r", Model = "m" });
        await _dbContext.SaveChangesAsync();
        Assert.Equal(2, await _dbContext.PipelineRuns.CountAsync(r => r.IssueNumber == 99));
    }
}
