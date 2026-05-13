using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class PullRequestPresenceGateTests
{
    [Fact]
    public async Task EvaluateAsync_OpenPullRequest_Passes()
    {
        var issueClient = new RecordingIssueClient
        {
            PullRequest = new GitHubPullRequestInfo(12, "https://github.com/owner/repo/pull/12", "cyberpilot/issue-7", "OPEN"),
        };
        var gate = new PullRequestPresenceGate(issueClient);

        var result = await gate.EvaluateAsync(Context());

        Assert.True(result.Passed);
        Assert.Contains("#12", result.Summary);
        Assert.Equal(7, issueClient.IssueNumber);
    }

    [Fact]
    public async Task EvaluateAsync_MissingPullRequest_FailsWithCorrectiveAction()
    {
        var gate = new PullRequestPresenceGate(new RecordingIssueClient());

        var result = await gate.EvaluateAsync(Context());

        Assert.False(result.Passed);
        Assert.True(result.IsRetryable);
        Assert.Contains("No open pull request", result.Summary);
        Assert.Equal(["Create or link a pull request for this issue before continuing."], result.RequiredActions);
    }

    [Fact]
    public async Task EvaluateAsync_ClosedPullRequest_FailsWithCorrectiveAction()
    {
        var issueClient = new RecordingIssueClient
        {
            PullRequest = new GitHubPullRequestInfo(12, "https://github.com/owner/repo/pull/12", "cyberpilot/issue-7", "CLOSED"),
        };
        var gate = new PullRequestPresenceGate(issueClient);

        var result = await gate.EvaluateAsync(Context());

        Assert.False(result.Passed);
        Assert.True(result.IsRetryable);
        Assert.Contains("not OPEN", result.Summary);
        Assert.Equal(["Reopen pull request #12 or create a new linked pull request."], result.RequiredActions);
    }

    [Fact]
    public void BuiltInPipelineGates_Create_RegistersPullRequestPresenceGate()
    {
        var gates = BuiltInPipelineGates.Create(new RecordingModelChecker(), new RecordingLabelService(), new RecordingIssueClient());

        Assert.True(gates.ContainsKey(BuiltInPipelineGates.PullRequestPresent));
        Assert.IsType<PullRequestPresenceGate>(gates[BuiltInPipelineGates.PullRequestPresent]);
    }

    private static PipelineGateContext Context()
    {
        var options = new CyberpilotOptions(7, Directory.GetCurrentDirectory(), "owner/repo", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        var executionContext = new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition);
        var stage = new PipelineStageDefinition(
            new StageDefinition("REVIEW", "review", "pipeline-review.agent.md", "sdk/review"),
            new StageContract(PipelineDefinitionDefaults.ContractVersion, []),
            [new GateDefinition(BuiltInPipelineGates.PullRequestPresent, GateTiming.BeforeStage, true)]);

        return new PipelineGateContext(executionContext, stage, stage.Gates[0]);
    }

    private sealed class RecordingIssueClient : IGitHubIssueClient
    {
        public GitHubPullRequestInfo? PullRequest { get; init; }
        public int IssueNumber { get; private set; }

        public Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueComment>>([]);
        public Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubIssueSummary?>(null);
        public Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult("OPEN");
        public Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueSummary>>([]);
        public Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            IssueNumber = issueNumber;
            return Task.FromResult(PullRequest);
        }
        public Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
}
