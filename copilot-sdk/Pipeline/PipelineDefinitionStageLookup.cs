namespace Cyberpilot.Pipeline;

internal static class PipelineDefinitionStageLookup
{
    public static StageDefinition Stage(this PipelineDefinition definition, string stageName)
    {
        return definition.PipelineStage(stageName).Stage;
    }

    public static PipelineStageDefinition PipelineStage(this PipelineDefinition definition, string stageName)
    {
        return definition.Stages.FirstOrDefault(candidate => candidate.Stage.Name.Equals(stageName, StringComparison.OrdinalIgnoreCase))
            ?? throw new UnknownPipelineStageException(stageName);
    }

    public static int IndexOf(this PipelineDefinition definition, string stageName)
    {
        for (var index = 0; index < definition.Stages.Count; index++)
        {
            if (definition.Stages[index].Stage.Name.Equals(stageName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    public static bool ShouldRun(this PipelineDefinition definition, PipelineStart start, StageDefinition stage)
    {
        var stageIndex = definition.IndexOf(stage.Name);
        return stageIndex >= start.Index;
    }
}