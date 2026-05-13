namespace Cyberpilot.Pipeline;

internal static class BuiltInPipelineDefinitions
{
    private static readonly IReadOnlyDictionary<string, PipelineDefinition> Definitions = new Dictionary<string, PipelineDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultPipelineDefinitionProvider.Definition.Name] = DefaultPipelineDefinitionProvider.Definition,
    };

    public static string AvailableNames => string.Join(", ", Definitions.Keys.Order(StringComparer.OrdinalIgnoreCase));

    public static bool TryGet(string name, out PipelineDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            definition = null;
            return false;
        }

        return Definitions.TryGetValue(name, out definition);
    }
}