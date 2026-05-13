using Cyberpilot.Copilot;
using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed class SdkCyberpilotRunner(
    CyberpilotOptions options,
    IGitHubIssueClient issueClient,
    ISdkLabelService labels,
    IBranchProvisioner branchProvisioner,
    IPromptBuilder promptBuilder,
    IStageRunner stageRunner,
    IModelAvailabilityChecker modelChecker,
    ICyberpilotProgressSink progressSink,
    TextWriter output)
{
    public string FinalStage { get; private set; } = "not-started";
    public string? BranchName { get; private set; }
    public string? PrUrl { get; private set; }
    public IReadOnlyList<StageResult> StageResults => stageResults;
    private readonly PipelineConsoleWriter console = new(output);
    private readonly StageExecutor stageExecutor = new(promptBuilder, stageRunner, progressSink, new PipelineConsoleWriter(output));
    private readonly PipelineBranchCoordinator branchCoordinator = new(options, issueClient, branchProvisioner, progressSink, new PipelineConsoleWriter(output));
    private readonly List<StageResult> stageResults = [];

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (options.CheckLabelsOnly)
        {
            console.WriteHeader("Label Preflight");
            console.WriteStep("Checking SDK labels");
            await labels.EnsureRequiredLabelsAsync(options.EnsureLabels, cancellationToken);
            console.WriteSuccess("SDK labels are ready. No stages were run.");
            return 0;
        }

        if (options.CheckModelOnly)
        {
            return await CheckModelAsync(cancellationToken);
        }

        console.WriteHeader($"Cyberpilot issue #{options.IssueNumber}");
        console.WriteDetail("Repository", options.RepoRoot);
        console.WriteDetail("Model", options.Model);
        console.WriteDetail("Stage timeout", PipelineConsoleWriter.FormatDuration(options.StageTimeout));

        var issueState = await issueClient.GetIssueStateAsync(options.IssueNumber, cancellationToken);
        if (issueState.Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            progressSink.OnDispatch(DispatchType.Skip, $"Issue #{options.IssueNumber} already closed — skipping");
            console.WriteWarning($"Issue #{options.IssueNumber} is already closed. Cyberpilot will not run or modify labels.");
            return 0;
        }

        await labels.EnsureRequiredLabelsAsync(options.EnsureLabels, cancellationToken);

        if (!options.ApproveAll)
        {
            progressSink.OnDispatch(DispatchType.Halt, "Missing --approve-all flag");
            console.WriteWarning("Cyberpilot requires --approve-all before running Copilot tool requests.");
            console.WriteDetail("Pilot tip", "Run with --skip-deliver first, and only use --approve-all in a trusted repository and issue context.");
            return 10;
        }

        var modelCheckExitCode = await CheckModelAsync(cancellationToken);
        if (modelCheckExitCode != 0)
        {
            progressSink.OnDispatch(DispatchType.Halt, $"Model unavailable: {options.Model}");
            return modelCheckExitCode;
        }

        progressSink.OnDispatch(DispatchType.Preflight, $"Model verified: {options.Model}");

        await labels.EnsureProvenanceAsync(options.IssueNumber, cancellationToken);
        progressSink.OnDispatch(DispatchType.Preflight, $"Labels ready — launching pipeline for issue #{options.IssueNumber}");

        return await RunPipelineAsync(cancellationToken);
    }

    private async Task<int> CheckModelAsync(CancellationToken cancellationToken)
    {
        console.WriteHeader("Model Preflight");
        console.WriteStep($"Checking Copilot model availability: {options.Model}");
        var result = await modelChecker.CheckAsync(options.Model, options.RepoRoot, cancellationToken);
        if (result.IsAvailable)
        {
            console.WriteSuccess($"Copilot model is available: {options.Model}");
            return 0;
        }

        console.WriteFailure($"Copilot model is not available: {options.Model}");
        console.WriteDetail("SDK error", result.Error ?? "No details returned.");
        console.WriteDetail("Next step", "Retry with --model <available-model-id>.");
        return 11;
    }

    private async Task<int> RunPipelineAsync(CancellationToken cancellationToken)
    {
        PipelineStart start;
        try
        {
            start = PipelineStartResolver.Resolve(options.StartStage);
        }
        catch (UnknownPipelineStageException ex)
        {
            return await HaltAsync(ex.Message, cancellationToken);
        }

        if (start.IsResume)
        {
            progressSink.OnDispatch(DispatchType.Routing, $"⏩ Resuming pipeline at {start.Stage.Name}");
            console.WriteHeader($"Resuming at stage: {start.Stage.Name}");
        }

        var routing = await branchCoordinator.ResolveStartAsync(start, cancellationToken);
        start = routing.Start;
        BranchName = routing.BranchName;
        PrUrl = routing.PrUrl;

        if (start.ShouldRun(StageCatalog.Triage))
        {
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Triage.Label, cancellationToken);
            var triage = await RunStageAsync(StageCatalog.Triage, "Classify the issue and publish the mandatory triage handoff comment.", cancellationToken);
            if (StageStatus.IsStop(triage))
            {
                progressSink.OnDispatch(DispatchType.Routing, "Triage returned STOP — the detective flagged this issue for human review before proceeding");
                console.WriteWarning("Triage returned STOP. Holding for human intervention.");
                await labels.ClearStageAsync(options.IssueNumber, cancellationToken);
                return 2;
            }

            if (!StageStatus.IsGo(triage) && !StageStatus.IsDuplicate(triage))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Triage returned unexpected status '{triage.Status}' — halting pipeline");
                return await HaltAsync($"Triage returned unexpected status '{triage.Status}'.", cancellationToken);
            }

            if (StageStatus.IsDuplicate(triage))
            {
                progressSink.OnDispatch(DispatchType.Routing, "Triage identified a duplicate — marking complete without implementation");
                console.WriteSuccess("Duplicate confirmed. Marking SDK pipeline complete without implementation.");
                await labels.SetStageAsync(options.IssueNumber, "sdk/done", cancellationToken);
                return 0;
            }

            progressSink.OnDispatch(DispatchType.Routing, "Triage cleared — case approved, dispatching to Plan stage");

            var pauseResult = await CheckPauseAsync("triage", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        BranchName = await branchCoordinator.EnsureBranchAsync(start, cancellationToken);

        if (start.ShouldRun(StageCatalog.Plan))
        {
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Plan.Label, cancellationToken);
            var plan = await RunStageAsync(StageCatalog.Plan, $"Create the implementation plan and issue comments for branch `{BranchName}`. The controller has already created or reused the branch; do not create a different branch.", cancellationToken);
            if (!StageStatus.IsGo(plan))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Plan returned '{plan.Status}' — halting pipeline");
                return await HaltAsync($"Plan returned '{plan.Status}'.", cancellationToken);
            }

            progressSink.OnDispatch(DispatchType.Routing, "Blueprint delivered — plan approved, dispatching to Implement stage");

            var pauseResult = await CheckPauseAsync("plan", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        if (start.ShouldRun(StageCatalog.Implement))
        {
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Implement.Label, cancellationToken);
            var implement = await RunStageAsync(StageCatalog.Implement, "Execute the plan, validate the changes, commit, push, create the PR, and post the build-complete issue comment.", cancellationToken);
            if (!StageStatus.IsGo(implement))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Implement returned '{implement.Status}' — halting pipeline");
                return await HaltAsync($"Implement returned '{implement.Status}'.", cancellationToken);
            }

            progressSink.OnDispatch(DispatchType.Routing, "Implementation complete — code committed and PR created, entering Review");

            var pauseResult = await CheckPauseAsync("implement", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        if (start.ShouldRun(StageCatalog.Review))
        {
            progressSink.OnDispatch(DispatchType.ReviewLoop, "Entering review loop — architecture, security, quality, and test coverage checks");

            var review = await RunReviewLoopAsync(cancellationToken);
            if (!StageStatus.IsGo(review))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Review returned '{review.Status}' — halting pipeline");
                return await HaltAsync($"Review returned '{review.Status}'.", cancellationToken);
            }

            if (!StageDecision.IsApproved(review))
            {
                progressSink.OnDispatch(DispatchType.Halt, "Review did not approve the changes — halting for human intervention");
                console.WriteWarning("Review did not return an approval. Halting before docs/deliver.");
                return 4;
            }

            progressSink.OnDispatch(DispatchType.Routing, "Review approved — all checks passed, dispatching to Docs stage");

            var pauseResult = await CheckPauseAsync("review", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        if (start.ShouldRun(StageCatalog.Docs))
        {
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Docs.Label, cancellationToken);
            var docs = await RunStageAsync(StageCatalog.Docs, "Update XML/markdown documentation and post the human verification walkthrough. Continue even if there are no docs changes.", cancellationToken);
            if (!StageStatus.IsGo(docs))
            {
                if (!options.AllowMissingDocs)
                {
                    progressSink.OnDispatch(DispatchType.Routing, $"Docs returned '{docs.Status}' — halting pipeline");
                    return await HaltAsync($"Docs returned '{docs.Status}'. Rerun with --allow-missing-docs to continue anyway.", cancellationToken);
                }

                progressSink.OnDispatch(DispatchType.Routing, $"Docs returned '{docs.Status}' but --allow-missing-docs is set — continuing to delivery");
                console.WriteWarning($"Docs returned '{docs.Status}', but --allow-missing-docs is set. Continuing.");
            }
            else
            {
                progressSink.OnDispatch(DispatchType.Routing, "Documentation updated — dispatching to Deliver stage");
            }

            var pauseResult = await CheckPauseAsync("docs", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        if (options.SkipDeliver)
        {
            progressSink.OnDispatch(DispatchType.Skip, "Skip-deliver enabled — pipeline complete, PR ready for manual merge");
            console.WriteSuccess("--skip-deliver set. Stopping before merge.");
            return 0;
        }

        await labels.SetStageAsync(options.IssueNumber, StageCatalog.Deliver.Label, cancellationToken);
        var deliverResult = await RunStageAsync(StageCatalog.Deliver, "Merge the approved PR, delete the feature branch, and post the landing report. Do not close the issue.", cancellationToken);
        if (!StageStatus.IsGo(deliverResult))
        {
            progressSink.OnDispatch(DispatchType.Routing, $"Deliver returned '{deliverResult.Status}' — merge may have failed");
            return await HaltAsync($"Deliver returned '{deliverResult.Status}'.", cancellationToken);
        }

        progressSink.OnDispatch(DispatchType.Routing, "Delivery complete — PR merged, branch cleaned up, landing report posted");
        await labels.SetStageAsync(options.IssueNumber, "sdk/done", cancellationToken);

        try
        {
            await issueClient.CloseIssueAsync(options.IssueNumber, cancellationToken);
            progressSink.OnDispatch(DispatchType.IssueClosed, $"Issue #{options.IssueNumber} closed — mission complete 🎯");
            console.WriteSuccess($"Issue #{options.IssueNumber} closed.");
        }
        catch (Exception ex)
        {
            console.WriteWarning($"Could not close issue #{options.IssueNumber}: {ex.Message}");
        }

        console.WriteSuccess("Landing confirmed. Airspace clear.");
        return 0;
    }

    private async Task<StageResult> RunReviewLoopAsync(CancellationToken cancellationToken)
    {
        StageResult review = StageResult.Empty;
        for (var cycle = 1; cycle <= 2; cycle++)
        {
            if (cycle > 1)
                progressSink.OnDispatch(DispatchType.ReviewLoop, $"Entering Review (round {cycle} of 2)");

            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Review.Label, cancellationToken);
            review = await RunStageAsync(StageCatalog.Review, $"Review the linked PR for issue #{options.IssueNumber}. This is review cycle {cycle} of 2.", cancellationToken);

            if (!StageStatus.IsGo(review))
            {
                return new StageResult("STOP", review.Decision, review.IsValid, review.Error);
            }

            if (!StageDecision.RequestsChanges(review))
            {
                return review;
            }

            if (cycle == 2)
            {
                progressSink.OnDispatch(DispatchType.ReviewLoop, "Review cycle 2/2 — max retries exhausted, halting for human review");
                console.WriteWarning("Review requested changes twice. Halting for human intervention.");
                return review;
            }

            progressSink.OnDispatch(DispatchType.ReviewLoop, "Review requested changes — cycling back to Implement");
            console.WriteStep("Review requested changes. Handing back to implementation for a go-around.");
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Implement.Label, cancellationToken);
            var rework = await RunStageAsync(StageCatalog.Implement, "Address the latest review findings, push fixes to the existing PR branch, and update the issue.", cancellationToken);
            if (!StageStatus.IsGo(rework))
            {
                return new StageResult("STOP", rework.Decision, rework.IsValid, rework.Error);
            }

            progressSink.OnDispatch(DispatchType.Routing, "Implement rework complete — returning to Review");
        }

        return review;
    }

    /// <summary>
    /// Checks whether the pipeline should pause after completing the given stage.
    /// Returns exit code 3 if pausing, or null to continue.
    /// </summary>
    private async Task<int?> CheckPauseAsync(string completedStage, CancellationToken cancellationToken)
    {
        if (options.ShouldPauseAsync is null) return null;
        if (!await options.ShouldPauseAsync(cancellationToken)) return null;

        progressSink.OnDispatch(DispatchType.Routing, $"⏸ Paused after {completedStage}");
        console.WriteStep($"Pipeline paused after {completedStage}.");
        return 3;
    }

    private async Task<int> HaltAsync(string reason, CancellationToken cancellationToken)
    {
        progressSink.OnDispatch(DispatchType.Halt, reason);
        console.WriteFailure($"Pipeline halted: {reason}");
        await labels.SetStageAsync(options.IssueNumber, "sdk/failed", cancellationToken);
        return 20;
    }

    private async Task<StageResult> RunStageAsync(StageDefinition stage, string mission, CancellationToken cancellationToken)
    {
        FinalStage = stage.Name;
        var result = await stageExecutor.RunAsync(stage, options.IssueNumber, options.StageTimeout, mission, cancellationToken);
        stageResults.Add(result);
        return result;
    }
}
