namespace Cyberpilot.Pipeline;

internal sealed class StageExecutor(
    IPromptBuilder promptBuilder,
    IStageRunner stageRunner,
    ICyberpilotProgressSink progressSink,
    PipelineConsoleWriter console)
{
    public async Task<StageResult> RunAsync(
        StageDefinition stage,
        int issueNumber,
        TimeSpan timeout,
        string mission,
        CancellationToken cancellationToken)
    {
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

        console.WriteSuccess($"Stage {stage.DisplayName} complete");
        progressSink.OnStageCompleted(stage, result);
        console.WriteDetail("Status", result.Status);
        console.WriteDetail("Decision", result.Decision);
        return result;
    }
}