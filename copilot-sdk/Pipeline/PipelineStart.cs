namespace Cyberpilot.Pipeline;

internal readonly record struct PipelineStart(int Index, StageDefinition Stage, bool IsResume)
{
    public static PipelineStart First => new(0, StageCatalog.All[0], false);

    public bool ShouldRun(StageDefinition stage)
    {
        var stageIndex = StageCatalog.IndexOf(stage.Name);
        return stageIndex >= Index;
    }
}