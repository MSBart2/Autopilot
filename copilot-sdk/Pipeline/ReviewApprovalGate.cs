namespace Cyberpilot.Pipeline;

internal sealed class ReviewApprovalGate : IPipelineGate
{
    public Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Stage.Stage.Name.Equals("review", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PipelineGateResult.Pass("Review approval gate is not applicable to this stage."));
        }

        if (context.StageResult is null)
        {
            return Task.FromResult(PipelineGateResult.Fail(
                "Review approval gate requires a completed review stage result.",
                requiredActions: ["Run this gate after the review stage has completed."]));
        }

        if (StageStatus.IsGo(context.StageResult) && StageDecision.IsApproved(context.StageResult))
        {
            return Task.FromResult(PipelineGateResult.Pass("Review approved the pull request."));
        }

        return Task.FromResult(PipelineGateResult.Fail(
            $"Review did not approve the pull request. Status: {context.StageResult.Status}; decision: {context.StageResult.Decision}.",
            isRetryable: StageDecision.RequestsChanges(context.StageResult),
            requiredActions: ["Address review findings and rerun the review stage."]));
    }
}
