namespace Cyberpilot.Pipeline;

internal interface IPipelineDefinitionProvider
{
    string AvailableNames { get; }

    bool TryGet(string name, out PipelineDefinition? definition);
}

internal sealed class BuiltInPipelineDefinitionProvider : IPipelineDefinitionProvider
{
    public string AvailableNames => BuiltInPipelineDefinitions.AvailableNames;

    public bool TryGet(string name, out PipelineDefinition? definition)
        => BuiltInPipelineDefinitions.TryGet(name, out definition);
}

internal sealed class CompositePipelineDefinitionProvider(IReadOnlyList<IPipelineDefinitionProvider> providers) : IPipelineDefinitionProvider
{
    public string AvailableNames => string.Join(", ", providers.Select(provider => provider.AvailableNames).Where(name => !string.IsNullOrWhiteSpace(name)));

    public bool TryGet(string name, out PipelineDefinition? definition)
    {
        foreach (var provider in providers)
        {
            if (provider.TryGet(name, out definition))
            {
                return true;
            }
        }

        definition = null;
        return false;
    }
}