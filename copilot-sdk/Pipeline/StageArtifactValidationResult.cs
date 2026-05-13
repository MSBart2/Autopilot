namespace Cyberpilot.Pipeline;

internal sealed record StageArtifactValidationResult(
    bool IsValid,
    string? Error = null,
    IReadOnlyList<string>? MissingArtifacts = null,
    IReadOnlyList<string>? RequiredActions = null)
{
    public static StageArtifactValidationResult Valid { get; } = new(true);
}
