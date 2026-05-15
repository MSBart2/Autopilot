using System.ComponentModel.DataAnnotations;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;

namespace Cyberpilot.Web.Models;

/// <summary>
/// Identifies which Cyberpilot runner is active on an issue.
/// </summary>
public enum CyberpilotRunnerType
{
    /// <summary>No active cyberpilot run.</summary>
    None,
    /// <summary>Running via GitHub Actions cloud workflow.</summary>
    Cloud,
    /// <summary>Running via local CLI.</summary>
    Local,
    /// <summary>Running via the SDK web dashboard.</summary>
    Sdk,
}

/// <summary>
/// Describes the current Cyberpilot state of an issue.
/// </summary>
public sealed record CyberpilotStatus(
    CyberpilotRunnerType Runner,
    string? Stage,
    bool IsActive,
    bool IsDone,
    bool IsFailed)
{
    /// <summary>No cyberpilot activity.</summary>
    public static CyberpilotStatus None { get; } = new(CyberpilotRunnerType.None, null, false, false, false);

    /// <summary>True when the issue has any cyberpilot-related label or DB run.</summary>
    public bool HasAny => Runner != CyberpilotRunnerType.None;

    /// <summary>Display name for the runner.</summary>
    public string RunnerLabel => Runner switch
    {
        CyberpilotRunnerType.Cloud => "Cloud",
        CyberpilotRunnerType.Local => "Local",
        CyberpilotRunnerType.Sdk => "SDK",
        _ => string.Empty,
    };

    /// <summary>Icon for the runner.</summary>
    public string RunnerIcon => Runner switch
    {
        CyberpilotRunnerType.Cloud => "☁️",
        CyberpilotRunnerType.Local => "💻",
        CyberpilotRunnerType.Sdk => "⚡",
        _ => string.Empty,
    };
}

/// <summary>
/// Displays pipeline runs on the dashboard.
/// </summary>
public sealed class PipelineDashboardViewModel
{
    /// <summary>
    /// Gets recent web-triggered SDK runs.
    /// </summary>
    public required IReadOnlyList<PipelineRun> Runs { get; init; }
}

/// <summary>
/// Displays open issues that can launch Cyberpilot.
/// </summary>
/// <param name="Issues">The open issues.</param>
/// <param name="Repository">The configured repository.</param>
/// <param name="RepositoryInput">The repository input shown in the launcher form.</param>
/// <param name="ConnectionId">The short-lived credential connection identifier.</param>
/// <param name="Error">An optional load error.</param>
/// <param name="ConfiguredRepositories">Configured repositories available in the launcher.</param>
/// <param name="SdkActiveIssueNumbers">Issue numbers with an active SDK web run in the DB.</param>
/// <param name="LatestSdkRunIds">Map of issue number to the most recent SDK run ID.</param>
/// <param name="PipelineDefinitions">Pipeline definitions available for SDK runs.</param>
/// <param name="PolicyProfiles">Policy profiles available for SDK runs.</param>
public sealed record PipelineIssuesViewModel(
    IReadOnlyList<GitHubIssueSummary> Issues,
    string Repository,
    string RepositoryInput,
    string? ConnectionId,
    string? Error,
    IReadOnlyList<ConfiguredRepositoryViewModel> ConfiguredRepositories,
    IReadOnlySet<int> SdkActiveIssueNumbers,
    IReadOnlyDictionary<int, string> LatestSdkRunIds,
    IReadOnlyList<PipelineDefinitionOptionViewModel> PipelineDefinitions,
    IReadOnlyList<PipelinePolicyOptionViewModel> PolicyProfiles)
{
    /// <summary>Initializes a view model without custom pipeline definition option data.</summary>
    public PipelineIssuesViewModel(
        IReadOnlyList<GitHubIssueSummary> issues,
        string repository,
        string repositoryInput,
        string? connectionId,
        string? error,
        IReadOnlyList<ConfiguredRepositoryViewModel> configuredRepositories,
        IReadOnlySet<int> sdkActiveIssueNumbers,
        IReadOnlyDictionary<int, string> latestSdkRunIds)
        : this(issues, repository, repositoryInput, connectionId, error, configuredRepositories, sdkActiveIssueNumbers, latestSdkRunIds, [], []) { }

    /// <summary>Initializes a view model without SDK run data.</summary>
    public PipelineIssuesViewModel(IReadOnlyList<GitHubIssueSummary> issues, string repository, string? error)
        : this(issues, repository, repository, null, error, [], new HashSet<int>(), new Dictionary<int, string>(), [], []) { }

    /// <summary>Gets the curated list of Copilot models available for selection.</summary>
    public static IReadOnlyList<string> AvailableModels { get; } =
    [
        "claude-sonnet-4.6",
        "claude-sonnet-4.5",
        "claude-haiku-4.5",
        "claude-opus-4.7",
        "claude-opus-4.6",
        "claude-opus-4.5",
        "gpt-4.1",
        "gpt-5-mini",
    ];

    /// <summary>
    /// Derives the full Cyberpilot status for an issue from its GitHub labels and the SDK DB.
    /// Priority: active runs > terminal states. Cloud > Local > SDK.
    /// </summary>
    public CyberpilotStatus GetStatus(GitHubIssueSummary issue)
    {
        var cloudStatus = ClassifyLabel(FindLabel(issue.Labels, "cloud/"), "cloud/", CyberpilotRunnerType.Cloud);
        var localStatus = ClassifyLabel(FindLabel(issue.Labels, "local/"), "local/", CyberpilotRunnerType.Local);
        var sdkStatus = ClassifyLabel(FindLabel(issue.Labels, "sdk/"), "sdk/", CyberpilotRunnerType.Sdk);

        if (sdkStatus is null && SdkActiveIssueNumbers.Contains(issue.Number))
            sdkStatus = new CyberpilotStatus(CyberpilotRunnerType.Sdk, "queued", true, false, false);

        return (cloudStatus, localStatus, sdkStatus) switch
        {
            ({ IsActive: true }, _, _) => cloudStatus!,
            (_, { IsActive: true }, _) => localStatus!,
            (_, _, { IsActive: true }) => sdkStatus!,
            ({ IsDone: true }, _, _) => cloudStatus!,
            (_, { IsDone: true }, _) => localStatus!,
            (_, _, { IsDone: true }) => sdkStatus!,
            ({ IsFailed: true }, _, _) => cloudStatus!,
            (_, { IsFailed: true }, _) => localStatus!,
            (_, _, { IsFailed: true }) => sdkStatus!,
            _ => CyberpilotStatus.None,
        };
    }

    private static string? FindLabel(IReadOnlyList<string> labels, string prefix)
        => labels.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsTerminalStage(string stage)
        => stage.Equals("done", StringComparison.OrdinalIgnoreCase)
        || stage.Equals("failed", StringComparison.OrdinalIgnoreCase);

    private static CyberpilotStatus? ClassifyLabel(string? rawLabel, string prefix, CyberpilotRunnerType runner)
    {
        if (rawLabel is null) return null;
        var stage = rawLabel[prefix.Length..];
        return new CyberpilotStatus(
            runner,
            stage,
            IsActive: !IsTerminalStage(stage),
            IsDone: stage.Equals("done", StringComparison.OrdinalIgnoreCase),
            IsFailed: stage.Equals("failed", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Displays a configured repository option without exposing its token.
/// </summary>
/// <param name="Name">The display name.</param>
/// <param name="Repository">The normalized owner/name repository.</param>
/// <param name="RepoRoot">The local git repository root.</param>
public sealed record ConfiguredRepositoryViewModel(string Name, string Repository, string RepoRoot);

/// <summary>
/// Captures a request to load issues from a GitHub repository.
/// </summary>
public sealed class PipelineIssueLoadRequest
{
    /// <summary>Gets or sets the repository URL or owner/name value.</summary>
    [Required]
    [StringLength(300)]
    public string RepositoryUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the GitHub token used to load issues and run Cyberpilot.</summary>
    [Required]
    [StringLength(300)]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Captures a request to load issues from a configured repository.
/// </summary>
public sealed class PipelineConfiguredIssueLoadRequest
{
    /// <summary>Gets or sets the configured repository in owner/name form.</summary>
    [Required]
    [StringLength(300)]
    public string Repository { get; set; } = string.Empty;
}

/// <summary>
/// Captures an operator decision note for a pipeline approval request.
/// </summary>
public sealed class PipelineApprovalDecisionRequest
{
    /// <summary>Gets or sets the optional decision reason supplied by the operator.</summary>
    [StringLength(1000)]
    public string? Reason { get; set; }
}
