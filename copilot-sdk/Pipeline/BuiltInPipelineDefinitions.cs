namespace Cyberpilot.Pipeline;

internal static class BuiltInPipelineDefinitions
{
    private static PipelineDefinition DocsOnlyDefinition { get; } = new(
        BuiltInPipelineCatalog.DocsOnlyDefinitionName,
        new PipelineDefinitionVersion(PipelineDefinitionDefaults.DefinitionVersion),
        new PolicyProfile(PipelineDefinitionDefaults.PolicyProfileName, PolicyStrictness.Standard),
        [
            DefaultPipelineDefinitionProvider.Definition.PipelineStage("docs"),
            DefaultPipelineDefinitionProvider.Definition.PipelineStage("deliver"),
        ],
        [new StageTransition("docs", "deliver", "GO")]);

    private static readonly IReadOnlyDictionary<string, PipelineDefinition> Definitions = new Dictionary<string, PipelineDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultPipelineDefinitionProvider.Definition.Name] = DefaultPipelineDefinitionProvider.Definition,
        [DocsOnlyDefinition.Name] = DocsOnlyDefinition,
    };

    public static string AvailableNames => BuiltInPipelineCatalog.AvailableDefinitionNames;

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