namespace Cyberpilot.Web.Services;

/// <summary>
/// Represents queued web work for the Cyberpilot background service.
/// </summary>
/// <param name="RunId">The persisted run identifier.</param>
/// <param name="IssueNumber">The GitHub issue number.</param>
/// <param name="Repository">The repository in owner/name form.</param>
/// <param name="RepoRoot">The local git repository root used for SDK execution.</param>
/// <param name="AgentPromptRoot">The repository root that contains Cyberpilot agent prompt files.</param>
/// <param name="GitHubToken">The GitHub token for this run. Not persisted to the database.</param>
/// <param name="Model">The Copilot model.</param>
/// <param name="SkipDeliver">Whether delivery should be skipped.</param>
/// <param name="StageTimeout">The per-stage timeout.</param>
/// <param name="AllowMissingDocs">Whether missing docs may be tolerated.</param>
public sealed record WebPipelineRunRequest(
    string RunId,
    int IssueNumber,
    string Repository,
    string RepoRoot,
    string AgentPromptRoot,
    string? GitHubToken,
    string Model,
    bool SkipDeliver,
    TimeSpan StageTimeout,
    bool AllowMissingDocs,
    string? StartStage = null);
