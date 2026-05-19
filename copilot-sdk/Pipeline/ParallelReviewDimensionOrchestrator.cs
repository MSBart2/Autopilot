using System.Diagnostics;

namespace Cyberpilot.Pipeline;

internal sealed class ParallelReviewDimensionOrchestrator(
    StageExecutor stageExecutor,
    ICyberpilotProgressSink progressSink,
    PipelineConsoleWriter console)
{
    private readonly SemaphoreSlim progressLock = new(1, 1);

    public async Task<StageResult> RunAsync(
        string reviewMission,
        int cycle,
        TimeSpan timeout,
        PolicyProfile policyProfile,
        PipelineExecutionContext context,
        CancellationToken cancellationToken)
    {
        var dimensions = ReviewDimensionDefinitions.Defaults;
        progressSink.OnDispatch(
            DispatchType.ReviewDimension,
            $"Parallel review enabled — dispatching {dimensions.Count} read-only dimensions: {string.Join(", ", dimensions.Select(d => d.Participant))}");
        console.WriteStep($"Parallel review dispatch: {dimensions.Count} read-only dimensions.");

        foreach (var dimension in dimensions)
        {
            progressSink.OnStageStarted(dimension.ToStage(), context.IssueNumber);
            progressSink.OnDispatch(
                DispatchType.ReviewDimension,
                $"Started review dimension '{dimension.Id}' with participant '{dimension.Participant}'.");
        }

        var totalStopwatch = Stopwatch.StartNew();
        var tasks = dimensions
            .Select(dimension => RunDimensionAsync(dimension, reviewMission, cycle, timeout, policyProfile, context, cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        totalStopwatch.Stop();

        var aggregate = ReviewDimensionAggregator.Aggregate(cycle, totalStopwatch.Elapsed, results);
        progressSink.OnDispatch(
            DispatchType.ReviewDimension,
            $"Parallel review merged {results.Length} dimensions in {PipelineConsoleWriter.FormatDuration(totalStopwatch.Elapsed)} — verdict: {aggregate.Decision}.");

        return aggregate;
    }

    private async Task<ReviewDimensionResult> RunDimensionAsync(
        ReviewDimensionDefinition dimension,
        string reviewMission,
        int cycle,
        TimeSpan timeout,
        PolicyProfile policyProfile,
        PipelineExecutionContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var stageDefinition = dimension.ToStageDefinition(PipelineDefinitionDefaults.ContractVersion);
        var sink = new BufferedStageProgressSink();
        StageResult result;
        try
        {
            result = await stageExecutor.RunAsync(
                stageDefinition,
                context.IssueNumber,
                timeout,
                BuildMission(dimension, reviewMission, cycle),
                policyProfile,
                context,
                cancellationToken,
                sink);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new StageResult(
                "STOP",
                "changes_requested",
                true,
                ex.Message,
                Evidence: [new StageEvidence($"review-dimension:{dimension.Id}:error", ex.Message)],
                PolicyRationale: $"The {dimension.DisplayName} review dimension failed closed.",
                RequiredActions: [$"Rerun or inspect the {dimension.DisplayName} review dimension failure: {ex.Message}"]);
        }
        finally
        {
            stopwatch.Stop();
        }

        await progressLock.WaitAsync(cancellationToken);
        try
        {
            progressSink.OnStageCompleted(dimension.ToStage(), result);
            progressSink.OnDispatch(
                DispatchType.ReviewDimension,
                $"Completed review dimension '{dimension.Id}' with {result.Status}/{result.Decision} in {PipelineConsoleWriter.FormatDuration(stopwatch.Elapsed)}.");
        }
        finally
        {
            progressLock.Release();
        }

        return new ReviewDimensionResult(dimension, result, stopwatch.Elapsed, sink.StreamedOutput);
    }

    private static string BuildMission(ReviewDimensionDefinition dimension, string reviewMission, int cycle)
    {
        return $$"""
            {{reviewMission}}

            Run only the {{dimension.DisplayName}} review dimension for review cycle {{cycle}}.

            Participant/persona: {{dimension.Participant}}
            Focus: {{dimension.Focus}}

            This is a read-only review dimension:
            - Do not edit files, commit, push, merge, close issues, set labels, post comments, submit GitHub PR reviews, or request external agents.
            - Use deterministic PR tools first (`get_pipeline_context`, `get_pr_details`, `get_pr_diff_summary`) and inspect only changed files or validation evidence relevant to this dimension.
            - Return a concise dimension report in the required `{{dimension.RequiredArtifact}}` artifact.
            - Use `decision: "changes_requested"` for any blocking or medium-or-higher finding under this dimension; otherwise use `decision: "approved"`.
            - Include concrete required actions with file and line references when requesting changes.
            """;
    }
}
