namespace Cyberpilot.Pipeline;

internal sealed class PipelineGateRunner(IReadOnlyDictionary<string, IPipelineGate> gates)
{
    public static PipelineGateRunner Empty { get; } = new(new Dictionary<string, IPipelineGate>(StringComparer.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<PipelineGateEvaluation>> RunAsync(
        PipelineExecutionContext executionContext,
        PipelineStageDefinition stage,
        GateTiming timing,
        StageResult? stageResult = null,
        CancellationToken cancellationToken = default)
    {
        var evaluations = new List<PipelineGateEvaluation>();
        foreach (var gate in stage.Gates.Where(gate => gate.Timing == timing))
        {
            if (!gates.TryGetValue(gate.Name, out var pipelineGate))
            {
                evaluations.Add(new PipelineGateEvaluation(
                    gate,
                    PipelineGateResult.Fail(
                        $"Gate '{gate.Name}' is declared for stage '{stage.Stage.Name}' but no evaluator is registered.",
                        requiredActions: [$"Register a deterministic evaluator for gate '{gate.Name}'."])));
                continue;
            }

            var result = await pipelineGate.EvaluateAsync(new PipelineGateContext(executionContext, stage, gate, stageResult), cancellationToken);
            evaluations.Add(new PipelineGateEvaluation(gate, result));
        }

        return evaluations;
    }
}
