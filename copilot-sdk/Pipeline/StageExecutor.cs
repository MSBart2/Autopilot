namespace Cyberpilot.Pipeline;

internal sealed class StageExecutor(
    IPromptBuilder promptBuilder,
    IStageRunner stageRunner,
    IStageArtifactValidator artifactValidator,
    ICyberpilotProgressSink progressSink,
    PipelineConsoleWriter console)
{
    public async Task<StageResult> RunAsync(
        PipelineStageDefinition stageDefinition,
        int issueNumber,
        TimeSpan timeout,
        string mission,
        PolicyProfile policyProfile,
        CancellationToken cancellationToken)
    {
        var stage = stageDefinition.Stage;
        console.WriteHeader($"Stage: {stage.DisplayName}");
        console.WriteDetail("Issue", $"#{issueNumber}");
        console.WriteDetail("Label", stage.Label);
        console.WriteDetail("Timeout", PipelineConsoleWriter.FormatDuration(timeout));

        progressSink.OnStageStarted(stage, issueNumber);
        var prompt = await promptBuilder.BuildAsync(stage, mission, cancellationToken);
        var result = await stageRunner.RunAsync(stage, prompt, timeout, cancellationToken);
        if (!result.IsValid)
        {
            console.WriteFailure($"Stage {stage.DisplayName} returned invalid JSON result: {result.Error}");
            return result;
        }

        var validation = artifactValidator.Validate(stageDefinition, result, policyProfile);
        if (!validation.IsValid)
        {
            var invalidResult = result with
            {
                Status = "INVALID",
                IsValid = false,
                Error = validation.Error,
                RequiredActions = validation.RequiredActions,
            };
            console.WriteFailure($"Stage {stage.DisplayName} failed artifact validation: {validation.Error}");
            progressSink.OnStageCompleted(stage, invalidResult);
            return invalidResult;
        }

        console.WriteSuccess($"Stage {stage.DisplayName} complete");
        progressSink.OnStageCompleted(stage, result);
        console.WriteDetail("Status", result.Status);
        console.WriteDetail("Decision", result.Decision);
        return result;
    }
}