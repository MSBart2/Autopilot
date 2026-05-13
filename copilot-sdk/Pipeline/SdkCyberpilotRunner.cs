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

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (options.CheckLabelsOnly)
        {
            WriteHeader("Label Preflight");
            WriteStep("Checking SDK labels");
            await labels.EnsureRequiredLabelsAsync(options.EnsureLabels, cancellationToken);
            WriteSuccess("SDK labels are ready. No stages were run.");
            return 0;
        }

        if (options.CheckModelOnly)
        {
            return await CheckModelAsync(cancellationToken);
        }

        WriteHeader($"Cyberpilot issue #{options.IssueNumber}");
        WriteDetail("Repository", options.RepoRoot);
        WriteDetail("Model", options.Model);
        WriteDetail("Stage timeout", FormatDuration(options.StageTimeout));

        var issueState = await issueClient.GetIssueStateAsync(options.IssueNumber, cancellationToken);
        if (issueState.Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            progressSink.OnDispatch(DispatchType.Skip, $"Issue #{options.IssueNumber} already closed — skipping");
            WriteWarning($"Issue #{options.IssueNumber} is already closed. Cyberpilot will not run or modify labels.");
            return 0;
        }

        await labels.EnsureRequiredLabelsAsync(options.EnsureLabels, cancellationToken);

        if (!options.ApproveAll)
        {
            progressSink.OnDispatch(DispatchType.Halt, "Missing --approve-all flag");
            WriteWarning("Cyberpilot requires --approve-all before running Copilot tool requests.");
            WriteDetail("Pilot tip", "Run with --skip-deliver first, and only use --approve-all in a trusted repository and issue context.");
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
        WriteHeader("Model Preflight");
        WriteStep($"Checking Copilot model availability: {options.Model}");
        var result = await modelChecker.CheckAsync(options.Model, options.RepoRoot, cancellationToken);
        if (result.IsAvailable)
        {
            WriteSuccess($"Copilot model is available: {options.Model}");
            return 0;
        }

        WriteFailure($"Copilot model is not available: {options.Model}");
        WriteDetail("SDK error", result.Error ?? "No details returned.");
        WriteDetail("Next step", "Retry with --model <available-model-id>.");
        return 11;
    }

    private static readonly string[] StageOrder = ["triage", "plan", "implement", "review", "docs", "deliver"];

    private async Task<int> RunPipelineAsync(CancellationToken cancellationToken)
    {
        var startIdx = 0;
        if (!string.IsNullOrEmpty(options.StartStage))
        {
            startIdx = Array.FindIndex(StageOrder, s => s.Equals(options.StartStage, StringComparison.OrdinalIgnoreCase));
            if (startIdx < 0)
            {
                return await HaltAsync($"Cannot resume from unknown stage '{options.StartStage}'.", cancellationToken);
            }
        }

        if (startIdx > 0)
        {
            progressSink.OnDispatch(DispatchType.Routing, $"⏩ Resuming pipeline at {StageOrder[startIdx]}");
            WriteHeader($"Resuming at stage: {StageOrder[startIdx]}");
        }

        // === EXISTING PR CHECK ===
        // If an open PR already exists for this issue, skip triage/plan/implement
        // and jump directly to review.
        if (startIdx == 0)
        {
            try
            {
                var existingPr = await issueClient.FindPullRequestForIssueAsync(options.IssueNumber, cancellationToken);
                if (existingPr is not null)
                {
                    progressSink.OnDispatch(DispatchType.Routing, $"Existing PR #{existingPr.Number} found for issue #{options.IssueNumber} — fast-forwarding to Review");
                    WriteSuccess($"Found open PR #{existingPr.Number} ({existingPr.HeadBranch}). Skipping triage/plan/implement.");
                    BranchName = existingPr.HeadBranch;
                    startIdx = 3; // jump to review
                }
            }
            catch (Exception ex)
            {
                WriteWarning($"Could not check for existing PRs: {ex.Message}");
                // Continue with normal triage — this is best-effort
            }
        }

        // === TRIAGE ===
        if (startIdx <= 0)
        {
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Triage.Label, cancellationToken);
            var triage = await RunStageAsync(StageCatalog.Triage, "Classify the issue and publish the mandatory triage handoff comment.", cancellationToken);
            if (triage.Status.Equals("STOP", StringComparison.OrdinalIgnoreCase))
            {
                progressSink.OnDispatch(DispatchType.Routing, "Triage returned STOP — the detective flagged this issue for human review before proceeding");
                WriteWarning("Triage returned STOP. Holding for human intervention.");
                await labels.ClearStageAsync(options.IssueNumber, cancellationToken);
                return 2;
            }

            if (!triage.Status.Equals("GO", StringComparison.OrdinalIgnoreCase) && !triage.Status.Equals("DUPLICATE", StringComparison.OrdinalIgnoreCase))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Triage returned unexpected status '{triage.Status}' — halting pipeline");
                return await HaltAsync($"Triage returned unexpected status '{triage.Status}'.", cancellationToken);
            }

            if (triage.Status.Equals("DUPLICATE", StringComparison.OrdinalIgnoreCase))
            {
                progressSink.OnDispatch(DispatchType.Routing, "Triage identified a duplicate — marking complete without implementation");
                WriteSuccess("Duplicate confirmed. Marking SDK pipeline complete without implementation.");
                await labels.SetStageAsync(options.IssueNumber, "sdk/done", cancellationToken);
                return 0;
            }

            progressSink.OnDispatch(DispatchType.Routing, "Triage cleared — case approved, dispatching to Plan stage");

            var pauseResult = await CheckPauseAsync("triage", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        // === BRANCH PROVISIONING ===
        // Always ensure the issue branch for all stages after triage.
        // EnsureBranchAsync is idempotent — reuses existing branches safely.
        {
            var issue = await issueClient.GetIssueAsync(options.IssueNumber, cancellationToken);
            var branch = await branchProvisioner.EnsureBranchAsync(
                options.Repository ?? string.Empty,
                options.IssueNumber,
                issue?.Title ?? $"issue-{options.IssueNumber}",
                options.RepoRoot,
                cancellationToken);
            BranchName = branch.BranchName;
            progressSink.OnBranchReady(branch.BranchName);

            if (startIdx == 0)
            {
                progressSink.OnDispatch(DispatchType.Branch, branch.WasCreated ? $"Created branch {branch.BranchName} for this issue" : $"Reusing existing branch {branch.BranchName}");
                WriteSuccess(branch.WasCreated
                    ? $"Created branch {branch.BranchName}."
                    : $"Using existing branch {branch.BranchName}.");
                await issueClient.CommentAsync(
                    options.IssueNumber,
                    $"SDK Cyberpilot branch ready: `{branch.BranchName}`. Planning and implementation will continue on this branch.",
                    cancellationToken);
            }
            else
            {
                progressSink.OnDispatch(DispatchType.Branch, $"Resuming work on existing branch {branch.BranchName}");
                WriteSuccess($"Resuming on existing branch {branch.BranchName}.");
            }
        }

        // === PLAN ===
        if (startIdx <= 1)
        {
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Plan.Label, cancellationToken);
            var plan = await RunStageAsync(StageCatalog.Plan, $"Create the implementation plan and issue comments for branch `{BranchName}`. The controller has already created or reused the branch; do not create a different branch.", cancellationToken);
            if (!plan.Status.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Plan returned '{plan.Status}' — halting pipeline");
                return await HaltAsync($"Plan returned '{plan.Status}'.", cancellationToken);
            }

            progressSink.OnDispatch(DispatchType.Routing, "Blueprint delivered — plan approved, dispatching to Implement stage");

            var pauseResult = await CheckPauseAsync("plan", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        // === IMPLEMENT ===
        if (startIdx <= 2)
        {
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Implement.Label, cancellationToken);
            var implement = await RunStageAsync(StageCatalog.Implement, "Execute the plan, validate the changes, commit, push, create the PR, and post the build-complete issue comment.", cancellationToken);
            if (!implement.Status.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Implement returned '{implement.Status}' — halting pipeline");
                return await HaltAsync($"Implement returned '{implement.Status}'.", cancellationToken);
            }

            progressSink.OnDispatch(DispatchType.Routing, "Implementation complete — code committed and PR created, entering Review");

            var pauseResult = await CheckPauseAsync("implement", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        // === REVIEW LOOP ===
        if (startIdx <= 3)
        {
            progressSink.OnDispatch(DispatchType.ReviewLoop, "Entering review loop — architecture, security, quality, and test coverage checks");

            var review = await RunReviewLoopAsync(cancellationToken);
            if (!review.Status.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Review returned '{review.Status}' — halting pipeline");
                return await HaltAsync($"Review returned '{review.Status}'.", cancellationToken);
            }

            if (!review.Decision.Equals("approved", StringComparison.OrdinalIgnoreCase))
            {
                progressSink.OnDispatch(DispatchType.Halt, "Review did not approve the changes — halting for human intervention");
                WriteWarning("Review did not return an approval. Halting before docs/deliver.");
                return 4;
            }

            progressSink.OnDispatch(DispatchType.Routing, "Review approved — all checks passed, dispatching to Docs stage");

            var pauseResult = await CheckPauseAsync("review", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        // === DOCS ===
        if (startIdx <= 4)
        {
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Docs.Label, cancellationToken);
            var docs = await RunStageAsync(StageCatalog.Docs, "Update XML/markdown documentation and post the human verification walkthrough. Continue even if there are no docs changes.", cancellationToken);
            if (!docs.Status.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.AllowMissingDocs)
                {
                    progressSink.OnDispatch(DispatchType.Routing, $"Docs returned '{docs.Status}' — halting pipeline");
                    return await HaltAsync($"Docs returned '{docs.Status}'. Rerun with --allow-missing-docs to continue anyway.", cancellationToken);
                }

                progressSink.OnDispatch(DispatchType.Routing, $"Docs returned '{docs.Status}' but --allow-missing-docs is set — continuing to delivery");
                WriteWarning($"Docs returned '{docs.Status}', but --allow-missing-docs is set. Continuing.");
            }
            else
            {
                progressSink.OnDispatch(DispatchType.Routing, "Documentation updated — dispatching to Deliver stage");
            }

            var pauseResult = await CheckPauseAsync("docs", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        // === DELIVER ===
        if (options.SkipDeliver)
        {
            progressSink.OnDispatch(DispatchType.Skip, "Skip-deliver enabled — pipeline complete, PR ready for manual merge");
            WriteSuccess("--skip-deliver set. Stopping before merge.");
            return 0;
        }

        await labels.SetStageAsync(options.IssueNumber, StageCatalog.Deliver.Label, cancellationToken);
        var deliverResult = await RunStageAsync(StageCatalog.Deliver, "Merge the approved PR, delete the feature branch, and post the landing report. Do not close the issue.", cancellationToken);
        if (!deliverResult.Status.Equals("GO", StringComparison.OrdinalIgnoreCase))
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
            WriteSuccess($"Issue #{options.IssueNumber} closed.");
        }
        catch (Exception ex)
        {
            WriteWarning($"Could not close issue #{options.IssueNumber}: {ex.Message}");
        }

        WriteSuccess("Landing confirmed. Airspace clear.");
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

            if (!review.Status.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                return new StageResult("STOP", review.Decision, review.IsValid, review.Error);
            }

            if (!review.Decision.Equals("changes_requested", StringComparison.OrdinalIgnoreCase))
            {
                return review;
            }

            if (cycle == 2)
            {
                progressSink.OnDispatch(DispatchType.ReviewLoop, "Review cycle 2/2 — max retries exhausted, halting for human review");
                WriteWarning("Review requested changes twice. Halting for human intervention.");
                return review;
            }

            progressSink.OnDispatch(DispatchType.ReviewLoop, "Review requested changes — cycling back to Implement");
            WriteStep("Review requested changes. Handing back to implementation for a go-around.");
            await labels.SetStageAsync(options.IssueNumber, StageCatalog.Implement.Label, cancellationToken);
            var rework = await RunStageAsync(StageCatalog.Implement, "Address the latest review findings, push fixes to the existing PR branch, and update the issue.", cancellationToken);
            if (!rework.Status.Equals("GO", StringComparison.OrdinalIgnoreCase))
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
        WriteStep($"Pipeline paused after {completedStage}.");
        return 3;
    }

    private async Task<int> HaltAsync(string reason, CancellationToken cancellationToken)
    {
        progressSink.OnDispatch(DispatchType.Halt, reason);
        WriteFailure($"Pipeline halted: {reason}");
        await labels.SetStageAsync(options.IssueNumber, "sdk/failed", cancellationToken);
        return 20;
    }

    private async Task<StageResult> RunStageAsync(StageDefinition stage, string mission, CancellationToken cancellationToken)
    {
        FinalStage = stage.Name;
        WriteHeader($"Stage: {stage.DisplayName}");
        WriteDetail("Issue", $"#{options.IssueNumber}");
        WriteDetail("Label", stage.Label);
        WriteDetail("Timeout", FormatDuration(options.StageTimeout));
        progressSink.OnStageStarted(stage, options.IssueNumber);
        var prompt = await promptBuilder.BuildAsync(stage, mission, cancellationToken);
        var result = await stageRunner.RunAsync(stage, prompt, options.StageTimeout, cancellationToken);
        if (!result.IsValid)
        {
            WriteFailure($"Stage {stage.DisplayName} returned invalid JSON result: {result.Error}");
            return result;
        }

        WriteSuccess($"Stage {stage.DisplayName} complete");
        progressSink.OnStageCompleted(stage, result);
        WriteDetail("Status", result.Status);
        WriteDetail("Decision", result.Decision);
        return result;
    }

    private void WriteHeader(string title)
    {
        output.WriteLine();
        output.WriteLine("============================================================");
        output.WriteLine(title);
        output.WriteLine("============================================================");
    }

    private void WriteStep(string message)
    {
        output.WriteLine($"[step] {message}");
    }

    private void WriteSuccess(string message)
    {
        output.WriteLine($"[ ok ] {message}");
    }

    private void WriteWarning(string message)
    {
        output.WriteLine($"[warn] {message}");
    }

    private void WriteFailure(string message)
    {
        output.WriteLine($"[fail] {message}");
    }

    private void WriteDetail(string name, string value)
    {
        output.WriteLine($"  {name,-14}: {value}");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes:0.##} min"
            : $"{duration.TotalSeconds:0.##} sec";
    }
}
