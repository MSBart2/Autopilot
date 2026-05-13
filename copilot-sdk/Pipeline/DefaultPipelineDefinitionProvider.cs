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
            Stage("TRIAGE", "triage", "triage.agent.md", "sdk/triage", ["triage-comment"]),
            Stage("PLAN", "plan", "plan.agent.md", "sdk/planning", ["plan-comment", "branch"]),
            Stage("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing", ["pull-request", "validation-summary"]),
            Stage("REVIEW", "review", "pipeline-review.agent.md", "sdk/review", ["review-verdict"]),
            Stage("DOCS", "docs", "docs.agent.md", "sdk/docs", ["documentation-summary"]),
            Stage("LAND", "deliver", "deliver.agent.md", "sdk/delivering", ["landing-report"]),
        ],
        [
            new StageTransition("triage", "plan", "GO"),
            new StageTransition("plan", "implement", "GO"),
            new StageTransition("implement", "review", "GO"),
            new StageTransition("review", "docs", "approved"),
            new StageTransition("review", "implement", "changes_requested"),
            new StageTransition("docs", "deliver", "GO"),
        ]);

    private static PipelineStageDefinition Stage(
        string displayName,
        string name,
        string promptFile,
        string label,
        IReadOnlyList<string> requiredArtifacts)
        => new(
            new StageDefinition(displayName, name, promptFile, label),
            new StageContract(ContractVersion, requiredArtifacts),
            []);
}
