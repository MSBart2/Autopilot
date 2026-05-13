namespace Cyberpilot.Pipeline;

internal sealed record PipelineGateContext(
    PipelineExecutionContext ExecutionContext,
    PipelineStageDefinition Stage,
    GateDefinition Gate,
    StageResult? StageResult = null);
