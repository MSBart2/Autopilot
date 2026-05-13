namespace Cyberpilot.Pipeline;

internal static class PipelineStartResolver
{
    public static PipelineStart Resolve(string? startStage)
        => Resolve(startStage, DefaultPipelineDefinitionProvider.Definition);

    public static PipelineStart Resolve(string? startStage, PipelineDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(startStage))
        {
            return new PipelineStart(0, definition.Stages[0].Stage, false);
        }

        var index = definition.IndexOf(startStage);
        if (index < 0)
        {
            throw new UnknownPipelineStageException(startStage);
        }

        return new PipelineStart(index, definition.Stages[index].Stage, true);
    }
}

internal sealed class UnknownPipelineStageException(string stageName)
    : Exception($"Cannot resume from unknown stage '{stageName}'.")
{
}