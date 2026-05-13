namespace Cyberpilot.Pipeline;

internal interface IPipelineGate
{
    Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default);
}
