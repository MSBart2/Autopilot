using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class RequiredLabelsGateTests
{
    [Fact]
    public async Task EvaluateAsync_RequiredLabelsAvailable_Passes()
    {
        var labels = new RecordingLabelService();
        var gate = new RequiredLabelsGate(labels);

        var result = await gate.EvaluateAsync(Context(ensureLabels: false));

        Assert.True(result.Passed);
        Assert.Equal(1, labels.EnsureRequiredCalls);
        Assert.False(labels.LastCreateMissing);
    }

    [Fact]
    public async Task EvaluateAsync_MissingLabels_FailsWithCorrectiveAction()
    {
        var labels = new RecordingLabelService { Error = new InvalidOperationException("Missing SDK labels: sdk/triage.") };
        var gate = new RequiredLabelsGate(labels);

        var result = await gate.EvaluateAsync(Context(ensureLabels: false));

        Assert.False(result.Passed);
        Assert.Contains("Missing SDK labels", result.Summary);
        Assert.Equal(["Create the missing SDK labels or rerun with --ensure-labels."], result.RequiredActions);
    }

    [Fact]
    public async Task EvaluateAsync_CreateMissingOption_ForwardsEnsureLabelsFlag()
    {
        var labels = new RecordingLabelService();
        var gate = new RequiredLabelsGate(labels);

        var result = await gate.EvaluateAsync(Context(ensureLabels: true));

        Assert.True(result.Passed);
        Assert.True(labels.LastCreateMissing);
    }

    [Fact]
    public void BuiltInPipelineGates_Create_RegistersRequiredLabelsGate()
    {
        var gates = BuiltInPipelineGates.Create(new RecordingModelChecker(), new RecordingLabelService(), new RecordingIssueClient());

        Assert.True(gates.ContainsKey(BuiltInPipelineGates.RequiredLabels));
        Assert.IsType<RequiredLabelsGate>(gates[BuiltInPipelineGates.RequiredLabels]);
    }

    private static PipelineGateContext Context(bool ensureLabels)
    {
        var options = new CyberpilotOptions(1, Directory.GetCurrentDirectory(), "owner/repo", "test-model", false, ensureLabels, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        var executionContext = new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition);
        var stage = new PipelineStageDefinition(
            new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage"),
            new StageContract(PipelineDefinitionDefaults.ContractVersion, []),
            [new GateDefinition(BuiltInPipelineGates.RequiredLabels, GateTiming.BeforeStage, true)]);

        return new PipelineGateContext(executionContext, stage, stage.Gates[0]);
    }

    private sealed class RecordingLabelService : ISdkLabelService
    {
        public int EnsureRequiredCalls { get; private set; }

        public bool LastCreateMissing { get; private set; }

        public InvalidOperationException? Error { get; init; }

        public Task EnsureProvenanceAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureRequiredLabelsAsync(bool createMissing, CancellationToken cancellationToken = default)
        {
            EnsureRequiredCalls++;
            LastCreateMissing = createMissing;
            if (Error is not null)
            {
                throw Error;
            }

            return Task.CompletedTask;
        }

        public Task ClearStageAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetStageAsync(int issueNumber, string stageLabel, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingModelChecker : Cyberpilot.Copilot.IModelAvailabilityChecker
    {
        public Task<Cyberpilot.Copilot.ModelAvailabilityResult> CheckAsync(string model, string repoRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(Cyberpilot.Copilot.ModelAvailabilityResult.Available);
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
