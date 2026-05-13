using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal static class PipelineDefinitionSelector
{
    public static bool TrySelect(CyberpilotOptions options, out PipelineDefinition? definition, out string? error)
    {
        definition = null;

        if (!options.PipelineDefinitionName.Equals(PipelineDefinitionDefaults.DefinitionName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported pipeline definition '{options.PipelineDefinitionName}'. Available definition: {PipelineDefinitionDefaults.DefinitionName}.";
            return false;
        }

        if (!options.PipelineDefinitionVersion.Equals(PipelineDefinitionDefaults.DefinitionVersion, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported pipeline definition version '{options.PipelineDefinitionVersion}' for {options.PipelineDefinitionName}. Available version: {PipelineDefinitionDefaults.DefinitionVersion}.";
            return false;
        }

        if (!options.PolicyProfileName.Equals(PipelineDefinitionDefaults.PolicyProfileName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported policy profile '{options.PolicyProfileName}' for {options.PipelineDefinitionName}. Available profile: {PipelineDefinitionDefaults.PolicyProfileName}.";
            return false;
        }

        definition = DefaultPipelineDefinitionProvider.Definition;
        var validationErrors = PipelineDefinitionValidator.Validate(definition);
        if (validationErrors.Count > 0)
        {
            error = $"Pipeline definition '{definition.Name}' is invalid: {string.Join(" ", validationErrors)}";
            definition = null;
            return false;
        }

        error = null;
        return true;
    }
}