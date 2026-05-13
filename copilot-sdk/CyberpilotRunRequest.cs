namespace Cyberpilot;

/// <summary>
/// Describes one Cyberpilot SDK pipeline run.
/// </summary>
public sealed record CyberpilotRunRequest(
    int IssueNumber,
    string RepoRoot,
    string? Repository,
    string? GitHubToken,
    string Model,
    bool SkipDeliver,
    TimeSpan StageTimeout,
    bool ApproveAll,
    bool AllowMissingDocs,
    bool EnsureLabels = false,
    string? AgentPromptRoot = null,
    string? StartStage = null,
    Func<CancellationToken, Task<bool>>? ShouldPauseAsync = null);
