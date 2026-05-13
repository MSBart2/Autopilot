namespace Cyberpilot.Pipeline;

internal readonly record struct PipelineStart(int Index, StageDefinition Stage, bool IsResume)
{
}