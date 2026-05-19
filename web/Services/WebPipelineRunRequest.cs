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
/// <param name="StartStage">The optional stage where execution should resume.</param>
/// <param name="PipelineDefinitionName">The pipeline definition selected for this run.</param>
/// <param name="PipelineDefinitionVersion">The pipeline definition version selected for this run.</param>
/// <param name="PolicyProfileName">The policy profile selected for this run.</param>
/// <param name="ContractVersion">The stage result contract version selected for this run.</param>
/// <param name="PipelineDefinitionFilePath">The optional JSON pipeline definition file path.</param>
/// <param name="RetryReason">The operator-provided retry reason for the next matching start stage.</param>
/// <param name="PrNumber">The known pull request number for PR-first review runs.</param>
/// <param name="StageModelOverrides">Per-stage model overrides for this queued run.</param>
/// <param name="StageModelFallbacks">Per-stage fallback models for this queued run.</param>
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
    string? StartStage = null,
    string? PipelineDefinitionName = null,
    string? PipelineDefinitionVersion = null,
    string? PolicyProfileName = null,
    string? ContractVersion = null,
    string? PipelineDefinitionFilePath = null,
    string? RetryReason = null,
    string? PrHeadBranch = null,
    int? PrNumber = null,
    IReadOnlyDictionary<string, string>? StageModelOverrides = null,
    IReadOnlyDictionary<string, string>? StageModelFallbacks = null);
