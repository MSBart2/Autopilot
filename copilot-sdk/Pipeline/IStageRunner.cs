namespace Cyberpilot.Pipeline;

internal interface IStageRunner
{
    Task<StageResult> RunAsync(
        StageDefinition stage,
        BuiltPrompt builtPrompt,
        TimeSpan timeout,
        string model,
        PipelineExecutionContext context,
        ICyberpilotProgressSink progressSink,
        CancellationToken cancellationToken = default);
}
