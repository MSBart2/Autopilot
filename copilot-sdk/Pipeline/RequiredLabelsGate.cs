using Cyberpilot.GitHub;

namespace Cyberpilot.Pipeline;

internal sealed class RequiredLabelsGate(ISdkLabelService labels) : IPipelineGate
{
    public async Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await labels.EnsureRequiredLabelsAsync(context.ExecutionContext.Options.EnsureLabels, cancellationToken);
            return PipelineGateResult.Pass("Required SDK labels are available.");
        }
        catch (InvalidOperationException ex)
        {
            return PipelineGateResult.Fail(
                ex.Message,
                requiredActions: ["Create the missing SDK labels or rerun with --ensure-labels."]);
        }
    }
}
