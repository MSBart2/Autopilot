namespace Cyberpilot.Pipeline;

internal sealed record ReviewDimensionResult(
    ReviewDimensionDefinition Dimension,
    StageResult Result,
    TimeSpan WallClockDuration,
    string StreamedOutput);
