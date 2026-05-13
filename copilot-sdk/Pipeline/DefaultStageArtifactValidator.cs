namespace Cyberpilot.Pipeline;

internal sealed class DefaultStageArtifactValidator : IStageArtifactValidator
{
    public StageArtifactValidationResult Validate(PipelineStageDefinition stage, StageResult result, PolicyProfile policyProfile)
    {
        if (!result.IsValid)
        {
            return StageArtifactValidationResult.Valid;
        }

        if (!string.IsNullOrWhiteSpace(result.ContractVersion)
            && !result.ContractVersion.Equals(stage.Contract.Version, StringComparison.OrdinalIgnoreCase))
        {
            return new StageArtifactValidationResult(
                false,
                $"Stage '{stage.Stage.Name}' returned contract version '{result.ContractVersion}', expected '{stage.Contract.Version}'.",
                RequiredActions: [$"Update the stage result to use contract version '{stage.Contract.Version}'."]);
        }

        if (stage.Contract.RequiredArtifacts.Count == 0 || result.Artifacts is null || result.Artifacts.Count == 0)
        {
            return StageArtifactValidationResult.Valid;
        }

        var artifactNames = result.Artifacts
            .Select(artifact => artifact.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingArtifacts = stage.Contract.RequiredArtifacts
            .Where(requiredArtifact => !artifactNames.Contains(requiredArtifact))
            .ToArray();

        if (missingArtifacts.Length == 0)
        {
            return StageArtifactValidationResult.Valid;
        }

        return new StageArtifactValidationResult(
            false,
            $"Stage '{stage.Stage.Name}' result is missing required artifact(s): {string.Join(", ", missingArtifacts)}.",
            missingArtifacts,
            missingArtifacts.Select(artifact => $"Include artifact '{artifact}' in the stage result.").ToArray());
    }
}
