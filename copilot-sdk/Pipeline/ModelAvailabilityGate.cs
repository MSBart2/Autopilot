using Cyberpilot.Copilot;

namespace Cyberpilot.Pipeline;

internal sealed class ModelAvailabilityGate(IModelAvailabilityChecker modelChecker) : IPipelineGate
{
    public async Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default)
    {
        var options = context.ExecutionContext.Options;
        var result = await modelChecker.CheckAsync(options.Model, options.RepoRoot, cancellationToken);
        if (result.IsAvailable)
        {
            return PipelineGateResult.Pass($"Model '{options.Model}' is available.");
        }

        return PipelineGateResult.Fail(
            $"Model '{options.Model}' is unavailable: {result.Error ?? "No details returned."}",
            requiredActions: [$"Retry with --model <available-model-id> instead of '{options.Model}'."]);
    }
}
