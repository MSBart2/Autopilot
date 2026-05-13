namespace Cyberpilot.Pipeline;

internal interface IStageArtifactValidator
{
    StageArtifactValidationResult Validate(PipelineStageDefinition stage, StageResult result, PolicyProfile policyProfile);
}
