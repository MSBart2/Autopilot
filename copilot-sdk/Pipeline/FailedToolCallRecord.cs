namespace Cyberpilot.Pipeline;

/// <summary>
/// Records a single failed tool call observed during a pipeline stage.
/// </summary>
/// <param name="ToolCallId">The unique identifier for the tool call.</param>
/// <param name="ToolName">The name of the tool that failed, when known.</param>
/// <param name="ErrorCode">The machine-readable error code, when reported.</param>
/// <param name="ErrorMessage">The human-readable error message, when reported.</param>
public sealed record FailedToolCallRecord(
    string ToolCallId,
    string? ToolName,
    string? ErrorCode,
    string? ErrorMessage);
