using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class PipelineEngineTests
{
    [Fact]
    public async Task ExecuteAsync_StructuredPauseWithApprovalRequest_NotifiesProgressSink()
    {
        var approvalRequest = new ApprovalGateRequest(
            "approval-engine-1",
            42,
            "triage",
            GateTiming.AfterStage,
            "Triage approval required before planning.",
            "maintainer",
            "plan",
            DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
        var options = new CyberpilotOptions(
            42,
            Directory.GetCurrentDirectory(),
            "owner/repo",
            "test-model",
            false,
            false,
            false,
            false,
            TimeSpan.FromMinutes(10),
            true,
            false,
            null,
            null,
            false,
            ShouldPauseDecisionAsync: (_, _) => Task.FromResult(PipelinePauseDecision.Pause("Approval required.", approvalRequest)));
        var context = new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition);
        var labels = new RecordingLabelService();
        var progressSink = new RecordingProgressSink();
        var console = new PipelineConsoleWriter(TextWriter.Null);
        var stageRunner = new RecordingStageRunner();
        var stageExecutor = new StageExecutor(new RecordingPromptBuilder(), stageRunner, new DefaultStageArtifactValidator(), progressSink, console);
        var branchCoordinator = new PipelineBranchCoordinator(options, new RecordingIssueClient(), new RecordingBranchProvisioner(), progressSink, console);
        var engine = new PipelineEngine(context, labels, branchCoordinator, stageExecutor, new PipelineGateRunner(new Dictionary<string, IPipelineGate>()), progressSink, console);

        var exitCode = await engine.ExecuteAsync(CancellationToken.None);

        Assert.Equal(3, exitCode);
        Assert.Single(progressSink.ApprovalRequests, approvalRequest);
        Assert.Contains(progressSink.Dispatches, dispatch => dispatch.Type == DispatchType.Approval && dispatch.Message.Contains("approval-engine-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BlockingGateFailure_RecordsStructuredEvidenceAndActions()
    {
        var options = new CyberpilotOptions(42, Directory.GetCurrentDirectory(), "owner/repo", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        var definition = DefinitionWithTriageGate();
        var context = new PipelineExecutionContext(options, definition);
        var labels = new RecordingLabelService();
        var progressSink = new RecordingProgressSink();
        var console = new PipelineConsoleWriter(TextWriter.Null);
        var stageRunner = new RecordingStageRunner();
        var stageExecutor = new StageExecutor(new RecordingPromptBuilder(), stageRunner, new DefaultStageArtifactValidator(), progressSink, console);
        var branchCoordinator = new PipelineBranchCoordinator(options, new RecordingIssueClient(), new RecordingBranchProvisioner(), progressSink, console);
        var gateRunner = new PipelineGateRunner(new Dictionary<string, IPipelineGate>(StringComparer.OrdinalIgnoreCase)
        {
            ["policy-ready"] = new StaticGate(PipelineGateResult.Fail("Policy review is incomplete.", isRetryable: true, requiredActions: ["Complete policy review before triage."])),
        });
        var engine = new PipelineEngine(context, labels, branchCoordinator, stageExecutor, gateRunner, progressSink, console);

        var exitCode = await engine.ExecuteAsync(CancellationToken.None);

        Assert.Equal(20, exitCode);
        Assert.Equal(0, stageRunner.Calls);
        var result = Assert.Single(context.StageResults);
        Assert.Equal("INVALID", result.Status);
        Assert.False(result.IsValid);
        Assert.Contains("policy-ready", result.Error);
        var evidence = Assert.Single(result.Evidence!);
        Assert.Equal("gate:policy-ready", evidence.Name);
        Assert.Equal("Policy review is incomplete.", evidence.Summary);
        Assert.Equal("Deterministic gate 'policy-ready' blocked stage 'triage'.", result.PolicyRationale);
        Assert.Equal(["Complete policy review before triage."], result.RequiredActions);
        Assert.Contains("sdk/failed", labels.StageLabels);
        Assert.Contains(progressSink.Dispatches, dispatch => dispatch.Type == DispatchType.Gate && dispatch.Message.Contains("policy-ready", StringComparison.Ordinal));
    }

    private static PipelineDefinition DefinitionWithTriageGate()
    {
        var stages = DefaultPipelineDefinitionProvider.Definition.Stages
            .Select(stage => stage.Stage.Name == "triage"
                ? stage with { Gates = [new GateDefinition("policy-ready", GateTiming.BeforeStage, true)] }
                : stage)
            .ToArray();

        return DefaultPipelineDefinitionProvider.Definition with { Stages = stages };
    }

    private sealed class StaticGate(PipelineGateResult result) : IPipelineGate
    {
        public Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class RecordingPromptBuilder : IPromptBuilder
    {
        public Task<string> BuildAsync(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, CancellationToken cancellationToken = default) => Task.FromResult("prompt");
    }

    private sealed class RecordingStageRunner : IStageRunner
    {
        public int Calls { get; private set; }

        public Task<StageResult> RunAsync(StageDefinition stage, string prompt, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new StageResult("GO", "approved", true, null));
        }
    }

    private sealed class RecordingLabelService : ISdkLabelService
    {
        public List<string> StageLabels { get; } = [];

        public Task EnsureProvenanceAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureRequiredLabelsAsync(bool createMissing, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearStageAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetStageAsync(int issueNumber, string stageLabel, CancellationToken cancellationToken = default)
        {
            StageLabels.Add(stageLabel);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProgressSink : ICyberpilotProgressSink
    {
        public List<(string Type, string Message)> Dispatches { get; } = [];
        public List<ApprovalGateRequest> ApprovalRequests { get; } = [];

        public void OnStageStarted(StageDefinition stage, int issueNumber) { }
        public void OnStageCompleted(StageDefinition stage, StageResult result) { }
        public void OnBranchReady(string branchName) { }
        public void OnApprovalRequested(ApprovalGateRequest request) => ApprovalRequests.Add(request);
        public void OnMessage(string level, string message) { }
        public void OnStreamDelta(string content) { }
        public void OnDispatch(string type, string message) => Dispatches.Add((type, message));
    }

    private sealed class RecordingBranchProvisioner : IBranchProvisioner
    {
        public Task<CyberpilotBranchInfo> EnsureBranchAsync(string repository, int issueNumber, string issueTitle, string repoRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CyberpilotBranchInfo($"sdk/issue-{issueNumber}-test", false, false, null));
    }

    private sealed class RecordingIssueClient : IGitHubIssueClient
    {
        public Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueComment>>([]);
        public Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubIssueSummary?>(new GitHubIssueSummary(issueNumber, "Test issue", string.Empty, [], DateTimeOffset.UtcNow, "OPEN", false));
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
