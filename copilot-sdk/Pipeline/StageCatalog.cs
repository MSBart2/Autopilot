namespace Cyberpilot.Pipeline;

internal static class StageCatalog
{
    public static StageDefinition Triage { get; } = new("TRIAGE", "triage", "triage.agent.md", "sdk/triage");
    public static StageDefinition Plan { get; } = new("PLAN", "plan", "plan.agent.md", "sdk/planning");
    public static StageDefinition Implement { get; } = new("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing");
    public static StageDefinition Review { get; } = new("REVIEW", "review", "pipeline-review.agent.md", "sdk/review");
    public static StageDefinition Docs { get; } = new("DOCS", "docs", "docs.agent.md", "sdk/docs");
    public static StageDefinition Deliver { get; } = new("LAND", "deliver", "deliver.agent.md", "sdk/delivering");
}
