namespace Cyberpilot.Pipeline;

internal sealed record PipelineGateResult(
    bool Passed,
    string Summary,
    bool IsRetryable = false,
    IReadOnlyList<string>? RequiredActions = null)
{
    public static PipelineGateResult Pass(string summary) => new(true, summary);

    public static PipelineGateResult Fail(string summary, bool isRetryable = false, IReadOnlyList<string>? requiredActions = null) =>
        new(false, summary, isRetryable, requiredActions);
}
