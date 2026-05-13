using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class ReviewApprovalGateTests
{
    [Fact]
    public async Task EvaluateAsync_ApprovedReview_Passes()
    {
        var gate = new ReviewApprovalGate();

        var result = await gate.EvaluateAsync(Context(new StageResult("GO", "approved", true, null)));

        Assert.True(result.Passed);
        Assert.Contains("approved", result.Summary);
    }

    [Fact]
    public async Task EvaluateAsync_ChangesRequested_FailsRetryableWithAction()
    {
        var gate = new ReviewApprovalGate();

        var result = await gate.EvaluateAsync(Context(new StageResult("GO", "changes_requested", true, null)));

        Assert.False(result.Passed);
        Assert.True(result.IsRetryable);
        Assert.Contains("changes_requested", result.Summary);
        Assert.Equal(["Address review findings and rerun the review stage."], result.RequiredActions);
    }

    [Fact]
    public async Task EvaluateAsync_MissingStageResult_FailsWithAction()
    {
        var gate = new ReviewApprovalGate();

        var result = await gate.EvaluateAsync(Context(stageResult: null));

        Assert.False(result.Passed);
        Assert.Contains("requires a completed review stage result", result.Summary);
        Assert.Equal(["Run this gate after the review stage has completed."], result.RequiredActions);
    }

    [Fact]
    public async Task EvaluateAsync_NonReviewStage_PassesAsNotApplicable()
    {
        var gate = new ReviewApprovalGate();

        var result = await gate.EvaluateAsync(Context(new StageResult("GO", "approved", true, null), stageName: "docs"));

        Assert.True(result.Passed);
        Assert.Contains("not applicable", result.Summary);
    }

    [Fact]
    public void BuiltInPipelineGates_Create_RegistersReviewApprovalGate()
    {
        var gates = BuiltInPipelineGates.Create(new RecordingModelChecker(), new RecordingLabelService(), new RecordingIssueClient());

        Assert.True(gates.ContainsKey(BuiltInPipelineGates.ReviewApproved));
        Assert.IsType<ReviewApprovalGate>(gates[BuiltInPipelineGates.ReviewApproved]);
    }

    private static PipelineGateContext Context(StageResult? stageResult, string stageName = "review")
    {
        var options = new CyberpilotOptions(7, Directory.GetCurrentDirectory(), "owner/repo", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        var executionContext = new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition);
        var stage = new PipelineStageDefinition(
            new StageDefinition(stageName.ToUpperInvariant(), stageName, "pipeline-review.agent.md", "sdk/review"),
            new StageContract(PipelineDefinitionDefaults.ContractVersion, []),
            [new GateDefinition(BuiltInPipelineGates.ReviewApproved, GateTiming.AfterStage, true)]);

        return new PipelineGateContext(executionContext, stage, stage.Gates[0], stageResult);
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
