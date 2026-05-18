namespace Cyberpilot.Pipeline;

/// <summary>
/// Captures model, usage, tool, and session metrics observed while running one pipeline stage.
/// </summary>
/// <param name="Model">The model that reported usage for the stage, when known.</param>
/// <param name="InputTokens">The number of input tokens consumed by this stage, when available.</param>
/// <param name="OutputTokens">The number of output tokens produced by this stage, when available.</param>
/// <param name="CacheReadTokens">The number of cache read tokens reported by the model, when available.</param>
/// <param name="CacheWriteTokens">The number of cache write tokens reported by the model, when available.</param>
/// <param name="ReasoningTokens">The number of reasoning tokens reported by the model, when available.</param>
/// <param name="PremiumRequestCost">The premium request cost or multiplier reported by the SDK, when available.</param>
/// <param name="DurationMs">The accumulated model API duration in milliseconds, when available.</param>
/// <param name="TurnCount">The number of assistant turns observed.</param>
/// <param name="ToolCallCount">The number of tool execution starts observed.</param>
/// <param name="FailedToolCallCount">The number of tool executions that completed unsuccessfully.</param>
/// <param name="SessionErrorCount">The number of session error events observed.</param>
/// <param name="ReachedIdle">Whether the session emitted an idle event.</param>
/// <param name="WasAborted">Whether the session idle event reported an aborted session.</param>
/// <param name="ProviderCallIds">Provider request identifiers observed in usage or error events.</param>
/// <param name="ApiCallIds">API call identifiers observed in usage events.</param>
/// <param name="FailedToolCalls">Per-call failure details for failed tool executions, when available.</param>
public sealed record StageExecutionMetrics(
    string? Model = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CacheReadTokens = null,
    int? CacheWriteTokens = null,
    int? ReasoningTokens = null,
    double? PremiumRequestCost = null,
    double? DurationMs = null,
    int TurnCount = 0,
    int ToolCallCount = 0,
    int FailedToolCallCount = 0,
    int SessionErrorCount = 0,
    bool ReachedIdle = false,
    bool WasAborted = false,
    IReadOnlyList<string>? ProviderCallIds = null,
    IReadOnlyList<string>? ApiCallIds = null,
    IReadOnlyList<FailedToolCallRecord>? FailedToolCalls = null);