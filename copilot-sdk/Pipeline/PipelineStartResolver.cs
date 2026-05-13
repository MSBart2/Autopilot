namespace Cyberpilot.Pipeline;

internal static class PipelineStartResolver
{
    public static PipelineStart Resolve(string? startStage)
    {
        if (string.IsNullOrWhiteSpace(startStage))
        {
            return PipelineStart.First;
        }

        var index = StageCatalog.IndexOf(startStage);
        if (index < 0)
        {
            throw new UnknownPipelineStageException(startStage);
        }

        return new PipelineStart(index, StageCatalog.All[index], true);
    }
}

internal sealed class UnknownPipelineStageException(string stageName)
    : Exception($"Cannot resume from unknown stage '{stageName}'.")
{
}