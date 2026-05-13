using Cyberpilot.GitHub;
using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed class PipelineEngine(
    PipelineExecutionContext context,
    ISdkLabelService labels,
    PipelineBranchCoordinator branchCoordinator,
    StageExecutor stageExecutor,
    PipelineGateRunner gateRunner,
    ICyberpilotProgressSink progressSink,
    PipelineConsoleWriter console)
{
    private CyberpilotOptions Options => context.Options;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        PipelineStart start;
        try
        {
            start = PipelineStartResolver.Resolve(Options.StartStage, context.Definition);
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

        var routing = await branchCoordinator.ResolveStartAsync(start, context.Definition, cancellationToken);
        start = routing.Start;
        context.BranchName = routing.BranchName;
        context.PrUrl = routing.PrUrl;

        var triageStage = Stage("triage");
        var planStage = Stage("plan");
        var implementStage = Stage("implement");
        var reviewStage = Stage("review");
        var docsStage = Stage("docs");
        var deliverStage = Stage("deliver");

        if (ShouldRun(start, triageStage))
        {
            await labels.SetStageAsync(Options.IssueNumber, triageStage.Label, cancellationToken);
            var triage = await RunStageAsync(triageStage, "Classify the issue and publish the mandatory triage handoff comment.", cancellationToken);
            if (StageStatus.IsStop(triage))
            {
                progressSink.OnDispatch(DispatchType.Routing, "Triage returned STOP — the detective flagged this issue for human review before proceeding");
                console.WriteWarning("Triage returned STOP. Holding for human intervention.");
                await labels.ClearStageAsync(Options.IssueNumber, cancellationToken);
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
                await labels.SetStageAsync(Options.IssueNumber, "sdk/done", cancellationToken);
                return 0;
            }

            progressSink.OnDispatch(DispatchType.Routing, "Triage cleared — case approved, dispatching to Plan stage");

            var pauseResult = await CheckPauseAsync("triage", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        context.BranchName = await branchCoordinator.EnsureBranchAsync(start, cancellationToken);

        if (ShouldRun(start, planStage))
        {
            await labels.SetStageAsync(Options.IssueNumber, planStage.Label, cancellationToken);
            var plan = await RunStageAsync(planStage, $"Create the implementation plan and issue comments for branch `{context.BranchName}`. The controller has already created or reused the branch; do not create a different branch.", cancellationToken);
            if (!StageStatus.IsGo(plan))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Plan returned '{plan.Status}' — halting pipeline");
                return await HaltAsync($"Plan returned '{plan.Status}'.", cancellationToken);
            }

            progressSink.OnDispatch(DispatchType.Routing, "Blueprint delivered — plan approved, dispatching to Implement stage");

            var pauseResult = await CheckPauseAsync("plan", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        if (ShouldRun(start, implementStage))
        {
            await labels.SetStageAsync(Options.IssueNumber, implementStage.Label, cancellationToken);
            var implement = await RunStageAsync(implementStage, "Execute the plan, validate the changes, commit, push, create the PR, and post the build-complete issue comment.", cancellationToken);
            if (!StageStatus.IsGo(implement))
            {
                progressSink.OnDispatch(DispatchType.Routing, $"Implement returned '{implement.Status}' — halting pipeline");
                return await HaltAsync($"Implement returned '{implement.Status}'.", cancellationToken);
            }

            progressSink.OnDispatch(DispatchType.Routing, "Implementation complete — code committed and PR created, entering Review");

            var pauseResult = await CheckPauseAsync("implement", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        if (ShouldRun(start, reviewStage))
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

            docsStage = TransitionTarget("review", "approved");
            progressSink.OnDispatch(DispatchType.Routing, "Review approved — all checks passed, dispatching to Docs stage");

            var pauseResult = await CheckPauseAsync("review", cancellationToken);
            if (pauseResult.HasValue) return pauseResult.Value;
        }

        if (ShouldRun(start, docsStage))
        {
            await labels.SetStageAsync(Options.IssueNumber, docsStage.Label, cancellationToken);
            var docs = await RunStageAsync(docsStage, "Update XML/markdown documentation and post the human verification walkthrough. Continue even if there are no docs changes.", cancellationToken);
            if (!StageStatus.IsGo(docs))
            {
                if (!Options.AllowMissingDocs)
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

        if (Options.SkipDeliver)
        {
            progressSink.OnDispatch(DispatchType.Skip, "Skip-deliver enabled — pipeline complete, PR ready for manual merge");
            console.WriteSuccess("--skip-deliver set. Stopping before merge.");
            return 0;
        }

        await labels.SetStageAsync(Options.IssueNumber, deliverStage.Label, cancellationToken);
        var deliverResult = await RunStageAsync(deliverStage, "Merge the approved PR, delete the feature branch, and post the landing report. Do not close the issue.", cancellationToken);
        if (!StageStatus.IsGo(deliverResult))
        {
            progressSink.OnDispatch(DispatchType.Routing, $"Deliver returned '{deliverResult.Status}' — merge may have failed");
            return await HaltAsync($"Deliver returned '{deliverResult.Status}'.", cancellationToken);
        }

        progressSink.OnDispatch(DispatchType.Routing, "Delivery complete — PR merged, branch cleaned up, landing report posted");
        await labels.SetStageAsync(Options.IssueNumber, "sdk/done", cancellationToken);

        try
        {
            await branchCoordinator.CloseIssueAsync(cancellationToken);
            progressSink.OnDispatch(DispatchType.IssueClosed, $"Issue #{Options.IssueNumber} closed — mission complete 🎯");
            console.WriteSuccess($"Issue #{Options.IssueNumber} closed.");
        }
        catch (Exception ex)
        {
            console.WriteWarning($"Could not close issue #{Options.IssueNumber}: {ex.Message}");
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

            var reviewStage = Stage("review");
            await labels.SetStageAsync(Options.IssueNumber, reviewStage.Label, cancellationToken);
            review = await RunStageAsync(reviewStage, $"Review the linked PR for issue #{Options.IssueNumber}. This is review cycle {cycle} of 2.", cancellationToken);

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
            var implementStage = TransitionTarget("review", "changes_requested");
            await labels.SetStageAsync(Options.IssueNumber, implementStage.Label, cancellationToken);
            var rework = await RunStageAsync(implementStage, "Address the latest review findings, push fixes to the existing PR branch, and update the issue.", cancellationToken);
            if (!StageStatus.IsGo(rework))
            {
                return new StageResult("STOP", rework.Decision, rework.IsValid, rework.Error);
            }

            progressSink.OnDispatch(DispatchType.Routing, "Implement rework complete — returning to Review");
        }

        return review;
    }

    private async Task<int?> CheckPauseAsync(string completedStage, CancellationToken cancellationToken)
    {
        if (Options.ShouldPauseDecisionAsync is not null)
        {
            var pauseContext = new PipelinePauseContext(completedStage, Options.IssueNumber, context.BranchName, context.PrUrl);
            var decision = await Options.ShouldPauseDecisionAsync(pauseContext, cancellationToken);
            if (!decision.ShouldPause) return null;

            var message = decision.ApprovalRequest is null
                ? decision.Reason
                : $"{decision.Reason} Approval '{decision.ApprovalRequest.Id}' requested for role '{decision.ApprovalRequest.RequestedRole}'.";
            progressSink.OnDispatch(DispatchType.Approval, message);
            console.WriteStep(message);
            return 3;
        }

        if (Options.ShouldPauseAsync is null) return null;
        if (!await Options.ShouldPauseAsync(cancellationToken)) return null;

        progressSink.OnDispatch(DispatchType.Routing, $"⏸ Paused after {completedStage}");
        console.WriteStep($"Pipeline paused after {completedStage}.");
        return 3;
    }

    private async Task<int> HaltAsync(string reason, CancellationToken cancellationToken)
    {
        progressSink.OnDispatch(DispatchType.Halt, reason);
        console.WriteFailure($"Pipeline halted: {reason}");
        await labels.SetStageAsync(Options.IssueNumber, "sdk/failed", cancellationToken);
        return 20;
    }

    private async Task<StageResult> RunStageAsync(StageDefinition stage, string mission, CancellationToken cancellationToken)
    {
        context.FinalStage = stage.Name;
        var stageDefinition = context.Definition.PipelineStage(stage.Name);
        var beforeGateResult = await RunGatesAsync(stageDefinition, GateTiming.BeforeStage, cancellationToken);
        if (beforeGateResult is not null)
        {
            context.StageResults.Add(beforeGateResult);
            return beforeGateResult;
        }

        var result = await stageExecutor.RunAsync(stageDefinition, Options.IssueNumber, Options.StageTimeout, mission, context.Definition.PolicyProfile, cancellationToken);
        var afterGateResult = await RunGatesAsync(stageDefinition, GateTiming.AfterStage, cancellationToken, result);
        if (afterGateResult is not null)
        {
            context.StageResults.Add(afterGateResult);
            return afterGateResult;
        }

        context.StageResults.Add(result);
        return result;
    }

    private async Task<StageResult?> RunGatesAsync(PipelineStageDefinition stageDefinition, GateTiming timing, CancellationToken cancellationToken, StageResult? stageResult = null)
    {
        var evaluations = await gateRunner.RunAsync(context, stageDefinition, timing, stageResult, cancellationToken);
        foreach (var evaluation in evaluations)
        {
            var outcome = evaluation.Result.Passed ? "passed" : "failed";
            progressSink.OnDispatch(DispatchType.Gate, $"Gate '{evaluation.Gate.Name}' {outcome} for stage '{stageDefinition.Stage.Name}': {evaluation.Result.Summary}");
            if (!evaluation.Result.Passed && evaluation.Gate.IsBlocking)
            {
                var summary = $"Blocking gate '{evaluation.Gate.Name}' failed for stage '{stageDefinition.Stage.Name}': {evaluation.Result.Summary}";
                return new StageResult(
                    "INVALID",
                    "unknown",
                    false,
                    summary,
                    Evidence: [new StageEvidence($"gate:{evaluation.Gate.Name}", evaluation.Result.Summary)],
                    PolicyRationale: $"Deterministic gate '{evaluation.Gate.Name}' blocked stage '{stageDefinition.Stage.Name}'.",
                    RequiredActions: evaluation.Result.RequiredActions);
            }
        }

        return null;
    }

    private StageDefinition Stage(string name)
    {
        return context.Definition.Stage(name);
    }

    private bool ShouldRun(PipelineStart start, StageDefinition stage)
    {
        return context.Definition.ShouldRun(start, stage);
    }

    private StageDefinition TransitionTarget(string fromStage, string condition)
    {
        return context.Definition.TransitionTarget(fromStage, condition);
    }
}