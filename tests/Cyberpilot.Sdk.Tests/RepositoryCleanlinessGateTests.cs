using Cyberpilot.Git;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class RepositoryCleanlinessGateTests
{
    [Fact]
    public async Task EvaluateAsync_CleanRepository_Passes()
    {
        var checker = new RecordingRepositoryCleanlinessChecker(RepositoryCleanlinessResult.Clean);
        var gate = new RepositoryCleanlinessGate(checker);

        var result = await gate.EvaluateAsync(Context("C:\\Repos\\Target"));

        Assert.True(result.Passed);
        Assert.Contains("clean working tree", result.Summary);
        Assert.Equal("C:\\Repos\\Target", checker.RepoRoot);
    }

    [Fact]
    public async Task EvaluateAsync_DirtyRepository_FailsWithCorrectiveActions()
    {
        var checker = new RecordingRepositoryCleanlinessChecker(RepositoryCleanlinessResult.Dirty("Dirty files:\n?? smoke-test.html"));
        var gate = new RepositoryCleanlinessGate(checker);

        var result = await gate.EvaluateAsync(Context("C:\\Repos\\Target"));

        Assert.False(result.Passed);
        Assert.True(result.IsRetryable);
        Assert.Contains("uncommitted changes", result.Summary);
        Assert.Contains("smoke-test.html", result.Summary);
        Assert.NotNull(result.RequiredActions);
        Assert.Contains(result.RequiredActions, action => action.Contains("git stash", StringComparison.OrdinalIgnoreCase));
    }

    private static PipelineGateContext Context(string repoRoot)
    {
        var options = new CyberpilotOptions(1, repoRoot, "owner/repo", "gpt-test", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        var executionContext = new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition);
        var stage = DefaultPipelineDefinitionProvider.Definition.PipelineStage("triage");
        var gate = stage.Gates.Single(item => item.Name == BuiltInPipelineGates.RepositoryClean);

        return new PipelineGateContext(executionContext, stage, gate);
    }

    private sealed class RecordingRepositoryCleanlinessChecker(RepositoryCleanlinessResult result) : IRepositoryCleanlinessChecker
    {
        public string? RepoRoot { get; private set; }

        public Task<RepositoryCleanlinessResult> CheckAsync(string repoRoot, CancellationToken cancellationToken = default)
        {
            RepoRoot = repoRoot;
            return Task.FromResult(result);
        }
    }
}
