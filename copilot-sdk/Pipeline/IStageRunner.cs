namespace Cyberpilot.Pipeline;

internal interface IStageRunner
{
    Task<StageResult> RunAsync(StageDefinition stage, string prompt, TimeSpan timeout, string model, PipelineExecutionContext context, CancellationToken cancellationToken = default);
}
