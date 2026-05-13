namespace Cyberpilot.Pipeline;

internal sealed record PipelineDefinition(
    string Name,
    PipelineDefinitionVersion Version,
    PolicyProfile PolicyProfile,
    IReadOnlyList<PipelineStageDefinition> Stages,
    IReadOnlyList<StageTransition> Transitions);

internal sealed record PipelineDefinitionVersion(string Value);

internal sealed record PipelineStageDefinition(
    StageDefinition Stage,
    StageContract Contract,
    IReadOnlyList<GateDefinition> Gates);

internal sealed record StageContract(string Version, IReadOnlyList<string> RequiredArtifacts);

internal sealed record GateDefinition(string Name, GateTiming Timing, bool IsBlocking);

internal sealed record StageTransition(string FromStage, string ToStage, string Condition);

internal sealed record PolicyProfile(string Name, PolicyStrictness Strictness);

internal enum GateTiming
{
    BeforeStage,
    AfterStage,
}

internal enum PolicyStrictness
{
    Lenient,
    Standard,
    Strict,
    SecurityCritical,
}
