namespace Cyberpilot.Pipeline;

internal static class StageCatalog
{
    private static readonly IReadOnlyList<StageDefinition> DefaultStages = DefaultPipelineDefinitionProvider.Definition.Stages
        .Select(stage => stage.Stage)
        .ToArray();

    public static StageDefinition Triage { get; } = DefaultStages[0];
    public static StageDefinition Plan { get; } = DefaultStages[1];
    public static StageDefinition Implement { get; } = DefaultStages[2];
    public static StageDefinition Review { get; } = DefaultStages[3];
    public static StageDefinition Docs { get; } = DefaultStages[4];
    public static StageDefinition Deliver { get; } = DefaultStages[5];

    public static IReadOnlyList<StageDefinition> All { get; } = DefaultStages;

    public static int IndexOf(string stageName)
    {
        for (var index = 0; index < All.Count; index++)
        {
            if (All[index].Name.Equals(stageName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
