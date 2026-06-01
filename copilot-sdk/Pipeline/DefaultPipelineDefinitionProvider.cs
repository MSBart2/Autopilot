namespace Cyberpilot.Pipeline;

internal static class DefaultPipelineDefinitionProvider
{
    public const string DefinitionName = PipelineDefinitionDefaults.DefinitionName;
    public const string DefinitionVersion = PipelineDefinitionDefaults.DefinitionVersion;
    public const string ContractVersion = PipelineDefinitionDefaults.ContractVersion;
    public const string PolicyProfileName = PipelineDefinitionDefaults.PolicyProfileName;

    public static PipelineDefinition Definition { get; } = new(
        DefinitionName,
        new PipelineDefinitionVersion(DefinitionVersion),
        new PolicyProfile(PolicyProfileName, PolicyStrictness.Standard),
        [
            Stage("TRIAGE", "triage", "triage.agent.md", "sdk/triage", ["triage-comment"], PreStageGates()),
            Stage("PLAN", "plan", "plan.agent.md", "sdk/planning", ["plan-comment", "branch"], PreStageGates()),
            Stage("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing", ["pull-request", "validation-summary"], PreStageGates()),
            Stage("REVIEW", "review", "pipeline-review.agent.md", "sdk/review", ["review-verdict"], PreStageGates()),
            Stage("DOCS", "docs", "docs.agent.md", "sdk/docs", ["documentation-summary"], PreStageGates()),
            Stage("SUMMARY", "summary", "summary.agent.md", "sdk/summary", ["summary-report"], PreStageGates()),
            Stage("LAND", "deliver", "deliver.agent.md", "sdk/delivering", ["landing-report"], PreStageGates()),
        ],
        [
            new StageTransition("triage", "plan", "GO"),
            new StageTransition("plan", "implement", "GO"),
            new StageTransition("implement", "review", "GO"),
            new StageTransition("review", "docs", "approved"),
            new StageTransition("review", "implement", "changes_requested"),
            new StageTransition("docs", "summary", "GO"),
            new StageTransition("summary", "deliver", "GO"),
        ]);

    private static PipelineStageDefinition Stage(
        string displayName,
        string name,
        string promptFile,
        string label,
        IReadOnlyList<string> requiredArtifacts,
        params GateDefinition[] gates)
        => new(
            new StageDefinition(displayName, name, promptFile, label),
            new StageContract(ContractVersion, requiredArtifacts),
            gates);

    private static GateDefinition[] PreStageGates()
        => [new GateDefinition(BuiltInPipelineGates.RepositoryClean, GateTiming.BeforeStage, true)];
}
