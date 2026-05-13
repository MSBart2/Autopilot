namespace Cyberpilot.Pipeline;

internal sealed class BranchReadyGate : IPipelineGate
{
    public Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(context.ExecutionContext.BranchName))
        {
            return Task.FromResult(PipelineGateResult.Pass($"Branch '{context.ExecutionContext.BranchName}' is ready."));
        }

        return Task.FromResult(PipelineGateResult.Fail(
            "No pipeline branch is available in the execution context.",
            isRetryable: true,
            requiredActions: ["Provision or select a branch before running this stage."]));
    }
}
