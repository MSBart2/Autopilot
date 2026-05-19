using Cyberpilot.Pipeline;

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
    Func<CancellationToken, Task<bool>>? ShouldPauseAsync = null,
    string? PipelineDefinitionName = null,
    string? PipelineDefinitionVersion = null,
    string? PolicyProfileName = null,
    string? TargetRepositoryProfileSummary = null,
    string? PipelineDefinitionFilePath = null,
    Func<PipelinePauseContext, CancellationToken, Task<PipelinePauseDecision>>? ShouldPauseDecisionAsync = null,
    string? PrHeadBranch = null,
    int? PrNumber = null,
    IReadOnlyDictionary<string, string>? StageModelOverrides = null,
    IReadOnlyDictionary<string, string>? StageModelFallbacks = null,
    CyberpilotRuntimePreferences? RuntimePreferences = null,
    string? RunId = null);
