using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal static class PipelineDefinitionSelector
{
    public static bool TrySelect(CyberpilotOptions options, out PipelineDefinition? definition, out string? error)
    {
        definition = null;

        if (!BuiltInPipelineDefinitions.TryGet(options.PipelineDefinitionName, out var selectedDefinition))
        {
            error = $"Unsupported pipeline definition '{options.PipelineDefinitionName}'. Available definitions: {BuiltInPipelineDefinitions.AvailableNames}.";
            return false;
        }

        if (!options.PipelineDefinitionVersion.Equals(selectedDefinition!.Version.Value, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported pipeline definition version '{options.PipelineDefinitionVersion}' for {options.PipelineDefinitionName}. Available version: {selectedDefinition.Version.Value}.";
            return false;
        }

        if (!BuiltInPolicyProfiles.TryGet(options.PolicyProfileName, out var policyProfile))
        {
            error = $"Unsupported policy profile '{options.PolicyProfileName}' for {options.PipelineDefinitionName}. Available profiles: {BuiltInPolicyProfiles.AvailableNames}.";
            return false;
        }

        definition = selectedDefinition with { PolicyProfile = policyProfile };
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