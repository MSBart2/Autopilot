using Cyberpilot.Copilot;
using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class SdkCyberpilotRunnerTests
{
    [Fact]
    public async Task RunAsync_ClosedIssue_NoOpsBeforeLabelsAndStages()
    {
        var issueClient = new FakeIssueClient { IssueState = "CLOSED" };
        var labels = new FakeLabelService();
        var stageRunner = new FakeStageRunner();
        var modelChecker = new FakeModelChecker(ModelAvailabilityResult.Available);
        var output = new StringWriter();
        var runner = CreateRunner(issueClient, labels, stageRunner, modelChecker, output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains("already closed", output.ToString());
        Assert.Equal(0, labels.EnsureRequiredCalls);
        Assert.Equal(0, labels.EnsureProvenanceCalls);
        Assert.Empty(labels.StageLabels);
        Assert.Equal(0, stageRunner.Calls);
        Assert.Equal(0, modelChecker.Calls);
    }

    [Fact]
    public async Task RunAsync_UnavailableModel_StopsBeforeProvenanceAndStages()
    {
        var issueClient = new FakeIssueClient { IssueState = "OPEN" };
        var labels = new FakeLabelService();
        var stageRunner = new FakeStageRunner();
        var modelChecker = new FakeModelChecker(ModelAvailabilityResult.Unavailable("Model is not available."));
        var output = new StringWriter();
        var runner = CreateRunner(issueClient, labels, stageRunner, modelChecker, output, approveAll: true);

        var exitCode = await runner.RunAsync();

        Assert.Equal(11, exitCode);
        Assert.Contains("Copilot model is not available", output.ToString());
        Assert.Equal(1, labels.EnsureRequiredCalls);
        Assert.Equal(0, labels.EnsureProvenanceCalls);
        Assert.Empty(labels.StageLabels);
        Assert.Equal(0, stageRunner.Calls);
    }

    [Fact]
    public async Task RunAsync_CheckModelOnly_DoesNotTouchIssueOrLabels()
    {
        var issueClient = new FakeIssueClient { IssueState = "OPEN" };
        var labels = new FakeLabelService();
        var modelChecker = new FakeModelChecker(ModelAvailabilityResult.Available);
        var output = new StringWriter();
        var options = CreateOptions(issueNumber: 0, approveAll: false) with { CheckModelOnly = true };
        var runner = new SdkCyberpilotRunner(options, issueClient, labels, new FakeBranchProvisioner(), new FakePromptBuilder(), new FakeStageRunner(), modelChecker, new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, issueClient.StateCalls);
        Assert.Equal(0, labels.EnsureRequiredCalls);
        Assert.Equal(1, modelChecker.Calls);
    }

    [Fact]
    public async Task RunAsync_CheckLabelsOnly_DoesNotRunStages()
    {
        var labels = new FakeLabelService();
        var stageRunner = new FakeStageRunner();
        var options = CreateOptions(0, false) with { CheckLabelsOnly = true };
        var output = new StringWriter();
        var runner = new SdkCyberpilotRunner(options, new FakeIssueClient(), labels, new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, labels.EnsureRequiredCalls);
        Assert.Equal(0, stageRunner.Calls);
        Assert.Contains("SDK labels are ready", output.ToString());
    }

    [Fact]
    public async Task RunAsync_ApproveAllFalse_ReturnsExit10()
    {
        var output = new StringWriter();
        var runner = CreateRunner(new FakeIssueClient(), new FakeLabelService(), new FakeStageRunner(), new FakeModelChecker(ModelAvailabilityResult.Available), output, approveAll: false);

        var exitCode = await runner.RunAsync();

        Assert.Equal(10, exitCode);
        Assert.Contains("--approve-all", output.ToString());
    }

    [Fact]
    public async Task RunAsync_UnsupportedPipelineDefinition_StopsBeforeIssueAndLabels()
    {
        var issueClient = new FakeIssueClient { IssueState = "OPEN" };
        var labels = new FakeLabelService();
        var stageRunner = new FakeStageRunner();
        var modelChecker = new FakeModelChecker(ModelAvailabilityResult.Available);
        var output = new StringWriter();
        var options = CreateOptions(122, true) with { PipelineDefinitionName = "docs-only" };
        var runner = new SdkCyberpilotRunner(options, issueClient, labels, new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, modelChecker, new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(12, exitCode);
        Assert.Contains("Unsupported pipeline definition", output.ToString());
        Assert.Equal(0, issueClient.StateCalls);
        Assert.Equal(0, labels.EnsureRequiredCalls);
        Assert.Equal(0, modelChecker.Calls);
        Assert.Equal(0, stageRunner.Calls);
    }

    [Fact]
    public async Task RunAsync_TriageStop_ReturnsExit2()
    {
        var labels = new FakeLabelService();
        var stageRunner = new FakeStageRunner { ResultOverride = new StageResult("STOP", "unknown", true, null) };
        var output = new StringWriter();
        var runner = CreateRunner(new FakeIssueClient(), labels, stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), output, approveAll: true);

        var exitCode = await runner.RunAsync();

        Assert.Equal(2, exitCode);
        Assert.Contains("STOP", output.ToString());
        Assert.Equal(1, labels.ClearStageCalls);
        Assert.DoesNotContain("sdk/failed", labels.StageLabels);
    }

    [Fact]
    public async Task RunAsync_TriageDuplicate_MarksAsDone()
    {
        var labels = new FakeLabelService();
        var stageRunner = new FakeStageRunner { ResultOverride = new StageResult("DUPLICATE", "unknown", true, null) };
        var output = new StringWriter();
        var runner = CreateRunner(new FakeIssueClient(), labels, stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), output, approveAll: true);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains("sdk/done", labels.StageLabels);
    }

    [Fact]
    public async Task RunAsync_FullPipeline_SkipDeliver_StopsBeforeMerge()
    {
        var labels = new FakeLabelService();
        var stageRunner = new FakeStageRunner();
        var output = new StringWriter();
        var options = CreateOptions(42, true) with { SkipDeliver = true };
        var runner = new SdkCyberpilotRunner(options, new FakeIssueClient(), labels, new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains("--skip-deliver", output.ToString());
        Assert.DoesNotContain("sdk/delivering", labels.StageLabels);
    }

    [Fact]
    public async Task RunAsync_FullPipeline_Success_SetsAllStageLabels()
    {
        var labels = new FakeLabelService();
        var stageRunner = new FakeStageRunner();
        var output = new StringWriter();
        var runner = CreateRunner(new FakeIssueClient(), labels, stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), output, approveAll: true);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains("sdk/triage", labels.StageLabels);
        Assert.Contains("sdk/planning", labels.StageLabels);
        Assert.Contains("sdk/implementing", labels.StageLabels);
        Assert.Contains("sdk/review", labels.StageLabels);
        Assert.Contains("sdk/docs", labels.StageLabels);
        Assert.Contains("sdk/delivering", labels.StageLabels);
        Assert.Contains("sdk/done", labels.StageLabels);
    }

    [Fact]
    public async Task RunAsync_PlanFails_Halts()
    {
        var labels = new FakeLabelService();
        var stageRunner = new ConditionalStageRunner(stage => stage.Name == "triage"
            ? new StageResult("GO", "approved", true, null)
            : new StageResult("STOP", "unknown", true, null));
        var output = new StringWriter();
        var runner = CreateRunner(new FakeIssueClient(), labels, stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), output, approveAll: true);

        var exitCode = await runner.RunAsync();

        Assert.Equal(20, exitCode);
        Assert.Contains("sdk/failed", labels.StageLabels);
    }

    [Fact]
    public async Task RunAsync_StageResultMissingDeclaredArtifact_HaltsBeforeNextStage()
    {
        var labels = new FakeLabelService();
        var stageRunner = new ConditionalStageRunner(stage => stage.Name == "triage"
            ? new StageResult("GO", "approved", true, null, Artifacts: [new StageArtifact("unexpected-artifact")])
            : new StageResult("GO", "approved", true, null));
        var output = new StringWriter();
        var runner = CreateRunner(new FakeIssueClient(), labels, stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), output, approveAll: true);

        var exitCode = await runner.RunAsync();

        Assert.Equal(20, exitCode);
        Assert.Contains("failed artifact validation", output.ToString());
        Assert.Contains("sdk/failed", labels.StageLabels);
        Assert.DoesNotContain("sdk/planning", labels.StageLabels);
    }

    [Fact]
    public async Task RunAsync_ReviewNotApproved_ReturnsExit4()
    {
        var stageRunner = new ConditionalStageRunner(stage =>
        {
            if (stage.Name == "review")
            {
                return new StageResult("GO", "changes_requested", true, null);
            }
            return new StageResult("GO", "approved", true, null);
        });
        var output = new StringWriter();
        var runner = CreateRunner(new FakeIssueClient(), new FakeLabelService(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), output, approveAll: true);

        var exitCode = await runner.RunAsync();

        Assert.Equal(4, exitCode);
    }

    [Fact]
    public async Task RunAsync_ExistingPullRequest_FastForwardsToReview()
    {
        var issueClient = new FakeIssueClient
        {
            ExistingPullRequest = new GitHubPullRequestInfo(17, "https://github.com/example/repo/pull/17", "sdk/issue-122-test", "OPEN")
        };
        var stageRunner = new RecordingStageRunner(_ => new StageResult("GO", "approved", true, null));
        var output = new StringWriter();
        var options = CreateOptions(122, true) with { SkipDeliver = true };
        var runner = new SdkCyberpilotRunner(options, issueClient, new FakeLabelService(), new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal("https://github.com/example/repo/pull/17", runner.PrUrl);
        Assert.Equal("sdk/issue-122-test", runner.BranchName);
        Assert.Equal<string>(["review", "docs"], stageRunner.StageNames);
    }

    [Fact]
    public async Task RunAsync_StartStageDocs_RunsOnlyDocsBeforeSkipDeliver()
    {
        var stageRunner = new RecordingStageRunner(_ => new StageResult("GO", "approved", true, null));
        var output = new StringWriter();
        var options = CreateOptions(122, true) with { StartStage = "docs", SkipDeliver = true };
        var runner = new SdkCyberpilotRunner(options, new FakeIssueClient(), new FakeLabelService(), new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal<string>(["docs"], stageRunner.StageNames);
    }

    [Theory]
    [InlineData("triage", new[] { "triage", "plan", "implement", "review", "docs", "deliver" })]
    [InlineData("plan", new[] { "plan", "implement", "review", "docs", "deliver" })]
    [InlineData("implement", new[] { "implement", "review", "docs", "deliver" })]
    [InlineData("review", new[] { "review", "docs", "deliver" })]
    [InlineData("docs", new[] { "docs", "deliver" })]
    [InlineData("deliver", new[] { "deliver" })]
    public async Task RunAsync_StartStage_ResumesAtRequestedStage(string startStage, string[] expectedStages)
    {
        var stageRunner = new RecordingStageRunner(_ => new StageResult("GO", "approved", true, null));
        var output = new StringWriter();
        var options = CreateOptions(122, true) with { StartStage = startStage };
        var runner = new SdkCyberpilotRunner(options, new FakeIssueClient(), new FakeLabelService(), new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(expectedStages, stageRunner.StageNames);
        Assert.Contains($"Resuming at stage: {startStage}", output.ToString());
    }

    [Fact]
    public async Task RunAsync_DocsStopWithoutWaiver_HaltsBeforeDeliver()
    {
        var labels = new FakeLabelService();
        var stageRunner = new RecordingStageRunner(stage => stage.Name == "docs"
            ? new StageResult("STOP", "unknown", true, null)
            : new StageResult("GO", "approved", true, null));
        var output = new StringWriter();
        var runner = CreateRunner(new FakeIssueClient(), labels, stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), output, approveAll: true);

        var exitCode = await runner.RunAsync();

        Assert.Equal(20, exitCode);
        Assert.Equal<string>(["triage", "plan", "implement", "review", "docs"], stageRunner.StageNames);
        Assert.Contains("sdk/failed", labels.StageLabels);
        Assert.DoesNotContain("sdk/delivering", labels.StageLabels);
        Assert.Contains("--allow-missing-docs", output.ToString());
    }

    [Fact]
    public async Task RunAsync_DocsStopWithWaiver_ContinuesToDeliver()
    {
        var labels = new FakeLabelService();
        var stageRunner = new RecordingStageRunner(stage => stage.Name == "docs"
            ? new StageResult("STOP", "unknown", true, null)
            : new StageResult("GO", "approved", true, null));
        var output = new StringWriter();
        var options = CreateOptions(122, true) with { AllowMissingDocs = true };
        var runner = new SdkCyberpilotRunner(options, new FakeIssueClient(), labels, new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal<string>(["triage", "plan", "implement", "review", "docs", "deliver"], stageRunner.StageNames);
        Assert.Contains("sdk/delivering", labels.StageLabels);
        Assert.Contains("sdk/done", labels.StageLabels);
        Assert.Contains("--allow-missing-docs is set", output.ToString());
    }

    [Fact]
    public async Task RunAsync_ReviewRequestsChangesTwice_HaltsBeforeDocs()
    {
        var labels = new FakeLabelService();
        var reviewCalls = 0;
        var implementCalls = 0;
        var stageRunner = new RecordingStageRunner(stage =>
        {
            if (stage.Name == "review")
            {
                reviewCalls++;
                return new StageResult("GO", "changes_requested", true, null);
            }

            if (stage.Name == "implement")
            {
                implementCalls++;
            }

            return new StageResult("GO", "approved", true, null);
        });
        var output = new StringWriter();
        var runner = new SdkCyberpilotRunner(CreateOptions(122, true), new FakeIssueClient(), labels, new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(4, exitCode);
        Assert.Equal(2, reviewCalls);
        Assert.Equal(2, implementCalls);
        Assert.Equal<string>(["triage", "plan", "implement", "review", "implement", "review"], stageRunner.StageNames);
        Assert.DoesNotContain("docs", stageRunner.StageNames);
        Assert.DoesNotContain("sdk/docs", labels.StageLabels);
        Assert.Contains("Review requested changes twice", output.ToString());
    }

    [Fact]
    public async Task RunAsync_ShouldPauseAfterPlan_ReturnsExit3()
    {
        var stageRunner = new RecordingStageRunner(_ => new StageResult("GO", "approved", true, null));
        var output = new StringWriter();
        var pauseChecks = 0;
        var options = CreateOptions(122, true) with
        {
            ShouldPauseAsync = _ => Task.FromResult(++pauseChecks == 2)
        };
        var runner = new SdkCyberpilotRunner(options, new FakeIssueClient(), new FakeLabelService(), new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(3, exitCode);
        Assert.Equal<string>(["triage", "plan"], stageRunner.StageNames);
        Assert.Contains("paused after plan", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ReviewRequestsChanges_ReworksAndReviewsAgain()
    {
        var reviewCalls = 0;
        var implementCalls = 0;
        var stageRunner = new RecordingStageRunner(stage =>
        {
            if (stage.Name == "review")
            {
                reviewCalls++;
                return reviewCalls == 1
                    ? new StageResult("GO", "changes_requested", true, null)
                    : new StageResult("GO", "approved", true, null);
            }

            if (stage.Name == "implement")
            {
                implementCalls++;
            }

            return new StageResult("GO", "approved", true, null);
        });
        var output = new StringWriter();
        var options = CreateOptions(122, true) with { SkipDeliver = true };
        var runner = new SdkCyberpilotRunner(options, new FakeIssueClient(), new FakeLabelService(), new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, new FakeModelChecker(ModelAvailabilityResult.Available), new TextWriterProgressSink(output, TextWriter.Null), output);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(2, reviewCalls);
        Assert.Equal(2, implementCalls);
        Assert.Equal<string>(["triage", "plan", "implement", "review", "implement", "review", "docs"], stageRunner.StageNames);
    }

    private static SdkCyberpilotRunner CreateRunner(
        FakeIssueClient issueClient,
        FakeLabelService labels,
        IStageRunner stageRunner,
        FakeModelChecker modelChecker,
        TextWriter output,
        bool approveAll = false)
    {
        var options = CreateOptions(122, approveAll);
        return new SdkCyberpilotRunner(options, issueClient, labels, new FakeBranchProvisioner(), new FakePromptBuilder(), stageRunner, modelChecker, new TextWriterProgressSink(output, TextWriter.Null), output);
    }

    private static CyberpilotOptions CreateOptions(int issueNumber, bool approveAll)
    {
        return new CyberpilotOptions(issueNumber, Directory.GetCurrentDirectory(), "rbmathis/Cyberpilot", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), approveAll, false, null, null, false);
    }

    private sealed class FakeIssueClient : IGitHubIssueClient
    {
        public string IssueState { get; init; } = "OPEN";
        public GitHubPullRequestInfo? ExistingPullRequest { get; init; }
        public int StateCalls { get; private set; }

        public Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueComment>>([]);
        public Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubIssueSummary?>(new GitHubIssueSummary(issueNumber, "Test issue", string.Empty, [], DateTimeOffset.UtcNow, IssueState, false));
        public Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            StateCalls++;
            return Task.FromResult(IssueState);
        }

        public Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueSummary>>([]);
        public Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult(ExistingPullRequest);
    }

    private sealed class FakeBranchProvisioner : IBranchProvisioner
    {
        public Task<CyberpilotBranchInfo> EnsureBranchAsync(string repository, int issueNumber, string issueTitle, string repoRoot, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CyberpilotBranchInfo($"sdk/issue-{issueNumber}-test", false, false, null));
        }
    }

    private sealed class FakeLabelService : ISdkLabelService
    {
        public int EnsureRequiredCalls { get; private set; }
        public int EnsureProvenanceCalls { get; private set; }
        public int ClearStageCalls { get; private set; }
        public List<string> StageLabels { get; } = [];

        public Task EnsureProvenanceAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            EnsureProvenanceCalls++;
            return Task.CompletedTask;
        }

        public Task EnsureRequiredLabelsAsync(bool createMissing, CancellationToken cancellationToken = default)
        {
            EnsureRequiredCalls++;
            return Task.CompletedTask;
        }

        public Task ClearStageAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            ClearStageCalls++;
            return Task.CompletedTask;
        }

        public Task SetStageAsync(int issueNumber, string stageLabel, CancellationToken cancellationToken = default)
        {
            StageLabels.Add(stageLabel);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePromptBuilder : IPromptBuilder
    {
        public Task<string> BuildAsync(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"Prompt for {stageDefinition.Stage.Name}");
        }
    }

    private sealed class FakeStageRunner : IStageRunner
    {
        public int Calls { get; private set; }

        public TimeSpan? LastTimeout { get; private set; }

        public StageResult? ResultOverride { get; set; }

        public Task<StageResult> RunAsync(StageDefinition stage, string prompt, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastTimeout = timeout;
            return Task.FromResult(ResultOverride ?? new StageResult("GO", "approved", true, null));
        }
    }

    private sealed class ConditionalStageRunner(Func<StageDefinition, StageResult> resultFactory) : IStageRunner
    {
        public Task<StageResult> RunAsync(StageDefinition stage, string prompt, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(resultFactory(stage));
        }
    }

    private sealed class RecordingStageRunner(Func<StageDefinition, StageResult> resultFactory) : IStageRunner
    {
        public List<string> StageNames { get; } = [];

        public Task<StageResult> RunAsync(StageDefinition stage, string prompt, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            StageNames.Add(stage.Name);
            return Task.FromResult(resultFactory(stage));
        }
    }

    private sealed class FakeModelChecker(ModelAvailabilityResult result) : IModelAvailabilityChecker
    {
        public int Calls { get; private set; }

        public Task<ModelAvailabilityResult> CheckAsync(string model, string repoRoot, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
