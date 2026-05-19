namespace Cyberpilot.Pipeline;

internal sealed record ReviewDimensionDefinition(
    string Id,
    string DisplayName,
    string Participant,
    string PromptFile,
    string Focus)
{
    public string StageName => $"review:{Id}";

    public string RequiredArtifact => $"review-dimension-{Id}";

    public StageDefinition ToStage()
        => new($"REVIEW/{DisplayName.ToUpperInvariant()}", StageName, PromptFile, "sdk/review");

    public PipelineStageDefinition ToStageDefinition(string contractVersion)
        => new(ToStage(), new StageContract(contractVersion, [RequiredArtifact]), []);
}

internal static class ReviewDimensionDefinitions
{
    public static IReadOnlyList<ReviewDimensionDefinition> Defaults { get; } =
    [
        new("architecture", "Architecture", "pipeline-review", "pipeline-review.agent.md", "Architecture, MVC boundaries, dependency injection, persistence shape, and cross-component policy consistency."),
        new("security", "Security", "security-reviewer", "security-reviewer.agent.md", "Authentication, authorization, input validation, secret exposure, injection risk, and other OWASP/security issues."),
        new("quality", "Code Quality", "code-quality-reviewer", "code-quality-reviewer.agent.md", "Maintainability, naming, async usage, error handling, dead code, XML documentation, and .NET MVC code quality."),
        new("tests", "Tests & Build", "build-validator", "build-validator.agent.md", "Build health, validation evidence, dependency health, test coverage gaps, and regression-test risk."),
        new("docs", "Documentation", "docs", "docs.agent.md", "Documentation obligations, architecture reference updates, README/user-facing notes, and verification walkthrough needs."),
    ];
}
