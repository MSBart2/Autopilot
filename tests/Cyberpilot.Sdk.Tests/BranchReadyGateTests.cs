using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class BranchReadyGateTests
{
    [Fact]
    public async Task EvaluateAsync_BranchNamePresent_Passes()
    {
        var gate = new BranchReadyGate();

        var result = await gate.EvaluateAsync(Context(branchName: "cyberpilot/issue-7"));

        Assert.True(result.Passed);
        Assert.Contains("cyberpilot/issue-7", result.Summary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EvaluateAsync_BranchNameMissing_FailsRetryableWithAction(string? branchName)
    {
        var gate = new BranchReadyGate();

        var result = await gate.EvaluateAsync(Context(branchName));

        Assert.False(result.Passed);
        Assert.True(result.IsRetryable);
        Assert.Contains("No pipeline branch", result.Summary);
        Assert.Equal(["Provision or select a branch before running this stage."], result.RequiredActions);
    }

    [Fact]
    public void BuiltInPipelineGates_Create_RegistersBranchReadyGate()
    {
        var gates = BuiltInPipelineGates.Create(new RecordingModelChecker(), new RecordingLabelService(), new RecordingIssueClient());

        Assert.True(gates.ContainsKey(BuiltInPipelineGates.BranchReady));
        Assert.IsType<BranchReadyGate>(gates[BuiltInPipelineGates.BranchReady]);
    }

    private static PipelineGateContext Context(string? branchName)
    {
        var options = new CyberpilotOptions(7, Directory.GetCurrentDirectory(), "owner/repo", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        var executionContext = new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition)
        {
            BranchName = branchName,
        };
        var stage = new PipelineStageDefinition(
            new StageDefinition("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing"),
            new StageContract(PipelineDefinitionDefaults.ContractVersion, []),
            [new GateDefinition(BuiltInPipelineGates.BranchReady, GateTiming.BeforeStage, true)]);

        return new PipelineGateContext(executionContext, stage, stage.Gates[0]);
    }

    private sealed class RecordingModelChecker : Cyberpilot.Copilot.IModelAvailabilityChecker
    {
        public Task<Cyberpilot.Copilot.ModelAvailabilityResult> CheckAsync(string model, string repoRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(Cyberpilot.Copilot.ModelAvailabilityResult.Available);
    }

    private sealed class RecordingLabelService : ISdkLabelService
    {
        public Task EnsureProvenanceAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureRequiredLabelsAsync(bool createMissing, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearStageAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetStageAsync(int issueNumber, string stageLabel, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingIssueClient : IGitHubIssueClient
    {
        public Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueComment>>([]);
        public Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubIssueSummary?>(null);
        public Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult("OPEN");
        public Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueSummary>>([]);
        public Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubPullRequestInfo?>(null);
        public Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
