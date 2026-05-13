using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;

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
public sealed record PipelineIssuesViewModel(
    IReadOnlyList<GitHubIssueSummary> Issues,
    string Repository,
    string RepositoryInput,
    string? ConnectionId,
    string? Error,
    IReadOnlyList<ConfiguredRepositoryViewModel> ConfiguredRepositories,
    IReadOnlySet<int> SdkActiveIssueNumbers,
    IReadOnlyDictionary<int, string> LatestSdkRunIds)
{
    /// <summary>Initializes a view model without SDK run data.</summary>
    public PipelineIssuesViewModel(IReadOnlyList<GitHubIssueSummary> issues, string repository, string? error)
        : this(issues, repository, repository, null, error, [], new HashSet<int>(), new Dictionary<int, string>()) { }

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
        // Find the most relevant label for each prefix
        static string? FindLabel(IReadOnlyList<string> labels, string prefix)
            => labels.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        static string StageFrom(string label, string prefix)
            => label[prefix.Length..]; // e.g. "cloud/triage" → "triage"

        static bool IsTerminal(string stage)
            => stage.Equals("done", StringComparison.OrdinalIgnoreCase)
            || stage.Equals("failed", StringComparison.OrdinalIgnoreCase);

        CyberpilotStatus? Classify(string? rawLabel, string prefix, CyberpilotRunnerType runner)
        {
            if (rawLabel is null) return null;
            var stage = StageFrom(rawLabel, prefix);
            return new CyberpilotStatus(
                runner,
                stage,
                IsActive: !IsTerminal(stage),
                IsDone: stage.Equals("done", StringComparison.OrdinalIgnoreCase),
                IsFailed: stage.Equals("failed", StringComparison.OrdinalIgnoreCase));
        }

        var cloudStatus = Classify(FindLabel(issue.Labels, "cloud/"), "cloud/", CyberpilotRunnerType.Cloud);
        var localStatus = Classify(FindLabel(issue.Labels, "local/"), "local/", CyberpilotRunnerType.Local);
        var sdkStatus = Classify(FindLabel(issue.Labels, "sdk/"), "sdk/", CyberpilotRunnerType.Sdk);

        // DB-active SDK run (no label yet, e.g. failed before first label write)
        if (sdkStatus is null && SdkActiveIssueNumbers.Contains(issue.Number))
            sdkStatus = new CyberpilotStatus(CyberpilotRunnerType.Sdk, "queued", true, false, false);

        // Return active runs first (Cloud > Local > SDK), then terminal states
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


/// <summary>
/// Displays one pipeline run and its logs.
/// </summary>
/// <param name="Run">The pipeline run.</param>
/// <param name="Logs">The stage logs.</param>
/// <param name="Labels">GitHub issue labels (best-effort, may be empty).</param>
/// <param name="Issue">GitHub issue details for the run, when available.</param>
/// <param name="Dispatches">Orchestrator dispatch events for the spine.</param>
/// <param name="Approvals">Human approval requests for the run.</param>
/// <param name="Evidence">Structured evidence rows for the run.</param>
public sealed record PipelineRunDetailsViewModel(PipelineRun Run, IReadOnlyList<PipelineStageLog> Logs, IReadOnlyList<string> Labels, GitHubIssueSummary? Issue = null, IReadOnlyList<PipelineDispatch>? Dispatches = null, IReadOnlyList<PipelineApproval>? Approvals = null, IReadOnlyList<PipelineEvidence>? Evidence = null)
{
    /// <summary>Initializes a details view model without labels.</summary>
    public PipelineRunDetailsViewModel(PipelineRun run, IReadOnlyList<PipelineStageLog> logs)
        : this(run, logs, []) { }

    /// <summary>Gets the ordered list of valid pipeline stage names.</summary>
    public static IReadOnlyList<string> ValidStageNames { get; } = ["triage", "plan", "implement", "review", "docs", "deliver"];

    /// <summary>Gets or sets the maximum number of retry attempts allowed per stage per run.</summary>
    public int MaxStageRetries { get; init; } = 3;

    /// <summary>Gets the best available explanation and recovery guidance for a terminal stopped run.</summary>
    public PipelineStopDiagnostic? StopDiagnostic => PipelineStopDiagnostic.Create(Run, Logs, Dispatches ?? []);

    /// <summary>Gets approval requests formatted for display.</summary>
    public IReadOnlyList<PipelineApprovalViewModel> ApprovalItems => (Approvals ?? [])
        .OrderByDescending(approval => approval.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        .ThenBy(approval => approval.CreatedAt)
        .Select(PipelineApprovalViewModel.FromApproval)
        .ToArray();

    /// <summary>Gets evidence ledger rows formatted for display.</summary>
    public IReadOnlyList<PipelineEvidenceViewModel> EvidenceItems => (Evidence ?? [])
        .OrderBy(evidence => StageSortOrder(evidence.StageName))
        .ThenBy(evidence => evidence.StageName)
        .ThenBy(evidence => evidence.Kind)
        .ThenBy(evidence => evidence.CreatedAt)
        .Select(PipelineEvidenceViewModel.FromEvidence)
        .ToArray();

    /// <summary>Gets whether this run has a pending human approval request.</summary>
    public bool HasPendingApprovals => ApprovalItems.Any(approval => approval.IsPending);

    /// <summary>Gets whether this run has a rejected human approval request.</summary>
    public bool HasRejectedApprovals => ApprovalItems.Any(approval => approval.IsRejected);

    /// <summary>Gets the total input tokens across all stage logs.</summary>
    public int TotalInputTokens => Logs.Sum(l => l.InputTokens ?? 0);

    /// <summary>Gets the total output tokens across all stage logs.</summary>
    public int TotalOutputTokens => Logs.Sum(l => l.OutputTokens ?? 0);

    /// <summary>Gets the total estimated USD cost across all stage logs, or null if no cost data is available.</summary>
    public decimal? TotalEstimatedCostUsd => Logs.Any(l => l.EstimatedCostUsd.HasValue)
        ? Logs.Sum(l => l.EstimatedCostUsd ?? 0m)
        : null;

    /// <summary>Gets whether this terminal run can be requeued from its current stage.</summary>
    public bool CanContinue => Run.Status is "Failed" or "Stopped" or "Paused" or "Cancelled" && !HasPendingApprovals && !HasRejectedApprovals;

    /// <summary>Gets whether the run can be sent back to implementation after a blocked review.</summary>
    public bool CanReworkFromReview => !Run.IsRemote
        && Run.Status is "Failed" or "Stopped"
        && IsReviewStage(Run.CurrentStage ?? Logs
            .Where(log => PipelineStopDiagnostic.IsBlockedStatus(log.Status))
            .OrderByDescending(log => log.CompletedAt ?? log.StartedAt)
            .FirstOrDefault()?.StageName);

    /// <summary>Gets the pipeline definition label shown in run telemetry.</summary>
    public string PipelineDefinitionLabel
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Run.PipelineDefinitionName)
                ? PipelineDefinitionDefaults.DefinitionName
                : Run.PipelineDefinitionName;
            var version = string.IsNullOrWhiteSpace(Run.PipelineDefinitionVersion)
                ? PipelineDefinitionDefaults.DefinitionVersion
                : Run.PipelineDefinitionVersion;

            return $"{name} v{version}";
        }
    }

    /// <summary>Gets the policy profile label shown in run telemetry.</summary>
    public string PolicyProfileLabel => string.IsNullOrWhiteSpace(Run.PolicyProfileName)
        ? PipelineDefinitionDefaults.PolicyProfileName
        : Run.PolicyProfileName;

    /// <summary>Gets the stage contract version label shown in run telemetry.</summary>
    public string ContractVersionLabel => string.IsNullOrWhiteSpace(Run.ContractVersion)
        ? PipelineDefinitionDefaults.ContractVersion
        : Run.ContractVersion;

    /// <summary>Gets the retry attempt count for a specific stage (number of existing stage logs for that stage).</summary>
    public int GetStageRetryCount(string stageName)
        => Logs.Count(l => l.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets whether a specific stage can be retried (run is terminal, stage is known, retry count is below the cap).</summary>
    public bool CanRetryStage(string stageName, int maxStageRetries)
        => !Run.IsRemote
        && Run.Status is "Failed" or "Stopped" or "Cancelled" or "Paused"
        && ValidStageNames.Any(s => s.Equals(stageName, StringComparison.OrdinalIgnoreCase))
        && GetStageRetryCount(stageName) < maxStageRetries;

    private static bool IsReviewStage(string? stageName)
        => stageName?.Equals("review", StringComparison.OrdinalIgnoreCase) == true;

    private static int StageSortOrder(string stageName)
    {
        for (var index = 0; index < ValidStageNames.Count; index++)
        {
            if (ValidStageNames[index].Equals(stageName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}

/// <summary>
/// Displays one structured evidence ledger row for a pipeline run.
/// </summary>
/// <param name="StageName">The stage that produced the evidence.</param>
/// <param name="Kind">The evidence kind.</param>
/// <param name="Name">The evidence name.</param>
/// <param name="Summary">The evidence summary.</param>
/// <param name="Uri">A URI for evidence details, when available.</param>
/// <param name="MediaType">The evidence media type, when available.</param>
/// <param name="CreatedAt">When the evidence row was captured.</param>
public sealed record PipelineEvidenceViewModel(
    string StageName,
    string Kind,
    string Name,
    string Summary,
    string? Uri,
    string? MediaType,
    DateTime CreatedAt)
{
    /// <summary>Gets a compact display label for the stage.</summary>
    public string StageLabel => DisplayStage(StageName);

    /// <summary>Gets a compact display label for the evidence kind.</summary>
    public string KindLabel => Kind switch
    {
        "stage-evidence" => "Evidence",
        "stage-artifact" => "Artifact",
        "policy-rationale" => "Policy",
        "required-action" => "Action",
        "approval-request" => "Approval",
        "approval-decision" => "Decision",
        _ => string.IsNullOrWhiteSpace(Kind) ? "Evidence" : Kind,
    };

    /// <summary>Gets whether this evidence has a detail link.</summary>
    public bool HasUri => !string.IsNullOrWhiteSpace(Uri);

    /// <summary>Creates a display model from a persisted evidence row.</summary>
    /// <param name="evidence">The persisted evidence row.</param>
    /// <returns>The evidence display model.</returns>
    public static PipelineEvidenceViewModel FromEvidence(PipelineEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return new PipelineEvidenceViewModel(
            evidence.StageName,
            evidence.Kind,
            evidence.Name,
            evidence.Summary,
            evidence.Uri,
            evidence.MediaType,
            evidence.CreatedAt);
    }

    private static string DisplayStage(string stageName)
        => string.IsNullOrWhiteSpace(stageName)
            ? "Pipeline"
            : char.ToUpperInvariant(stageName[0]) + stageName[1..];
}

/// <summary>
/// Displays one human approval request for a pipeline run.
/// </summary>
/// <param name="Id">The approval identifier.</param>
/// <param name="StageName">The stage that requested approval.</param>
/// <param name="Timing">Whether approval was requested before or after the stage.</param>
/// <param name="Reason">The approval reason.</param>
/// <param name="RequestedRole">The role requested to decide the approval.</param>
/// <param name="ResumeStageName">The stage to resume after approval.</param>
/// <param name="Status">The approval status.</param>
/// <param name="CreatedAt">When the approval was requested.</param>
/// <param name="DecidedBy">Who decided the approval, when decided.</param>
/// <param name="DecisionReason">The decision reason, when provided.</param>
/// <param name="DecidedAt">When the approval was decided.</param>
public sealed record PipelineApprovalViewModel(
    string Id,
    string StageName,
    string Timing,
    string Reason,
    string RequestedRole,
    string ResumeStageName,
    string Status,
    DateTime CreatedAt,
    string? DecidedBy,
    string? DecisionReason,
    DateTime? DecidedAt)
{
    /// <summary>Gets whether this approval is still pending.</summary>
    public bool IsPending => Status.Equals("Pending", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets whether this approval has been approved.</summary>
    public bool IsApproved => Status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets whether this approval has been rejected.</summary>
    public bool IsRejected => Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets a compact display label for the approval stage and timing.</summary>
    public string StageTimingLabel => $"{DisplayStage(StageName)} · {DisplayTiming(Timing)}";

    /// <summary>Creates a display model from a persisted approval row.</summary>
    /// <param name="approval">The persisted approval row.</param>
    /// <returns>The approval display model.</returns>
    public static PipelineApprovalViewModel FromApproval(PipelineApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);

        return new PipelineApprovalViewModel(
            approval.Id,
            approval.StageName,
            approval.Timing,
            approval.Reason,
            approval.RequestedRole,
            approval.ResumeStageName,
            approval.Status,
            approval.CreatedAt,
            approval.DecidedBy,
            approval.DecisionReason,
            approval.DecidedAt);
    }

    private static string DisplayStage(string stageName)
        => string.IsNullOrWhiteSpace(stageName)
            ? "Pipeline"
            : char.ToUpperInvariant(stageName[0]) + stageName[1..];

    private static string DisplayTiming(string timing)
        => timing switch
        {
            "BeforeStage" => "before stage",
            "AfterStage" => "after stage",
            _ => string.IsNullOrWhiteSpace(timing) ? "approval" : timing,
        };
}

/// <summary>
/// Explains why a pipeline stopped or failed and what the operator can do next.
/// </summary>
/// <param name="Severity">The Bootstrap alert severity to use for the diagnostic.</param>
/// <param name="Title">The short diagnostic title.</param>
/// <param name="Reason">The best available stoppage reason.</param>
/// <param name="CorrectiveActions">Recommended corrective actions for the operator.</param>
/// <param name="Evidence">Optional supporting evidence from the run log or dispatch stream.</param>
public sealed record PipelineStopDiagnostic(
    string Severity,
    string Title,
    string Reason,
    IReadOnlyList<string> CorrectiveActions,
    string? Evidence)
{
    private static readonly Regex FencedJsonRegex = new("```json\\s*(?<json>\\{.*?\\})\\s*```", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Builds a diagnostic from existing run, stage-log, and dispatch data.
    /// </summary>
    /// <param name="run">The pipeline run to diagnose.</param>
    /// <param name="logs">Stage logs recorded for the run.</param>
    /// <param name="dispatches">Orchestrator dispatch events recorded for the run.</param>
    /// <returns>A diagnostic for terminal blocked runs; otherwise <see langword="null" />.</returns>
    public static PipelineStopDiagnostic? Create(PipelineRun run, IReadOnlyList<PipelineStageLog> logs, IReadOnlyList<PipelineDispatch> dispatches)
    {
        if (run.Status is not ("Failed" or "Stopped"))
        {
            return null;
        }

        var blockedLog = logs
            .Where(log => IsBlockedStatus(log.Status))
            .OrderByDescending(log => log.CompletedAt ?? log.StartedAt)
            .FirstOrDefault();
        var summary = TryReadSummary(blockedLog?.Output);
        var haltDispatch = dispatches
            .Where(IsDiagnosticDispatch)
            .OrderByDescending(dispatch => dispatch.CreatedAt)
            .FirstOrDefault();

        var reason = FirstNonEmpty(
            run.Error,
            ReadSummaryText(summary, "error", "stop_reason", "reason", "summary"),
            haltDispatch?.Message,
            blockedLog is null ? null : $"{DisplayStage(blockedLog.StageName)} returned {blockedLog.Status}.",
            run.Status == "Stopped" ? "The pipeline stopped before it could safely continue." : "The pipeline failed before completion.")!;

        var stageName = blockedLog?.StageName ?? run.CurrentStage;
        var actions = BuildCorrectiveActions(reason, stageName, summary);
        var evidence = BuildEvidence(haltDispatch, blockedLog, summary);

        return new PipelineStopDiagnostic(
            run.Status == "Failed" ? "danger" : "warning",
            BuildTitle(run.Status, stageName),
            reason,
            actions,
            evidence);
    }

    internal static bool IsBlockedStatus(string? status)
        => status?.Equals("STOP", StringComparison.OrdinalIgnoreCase) == true
        || status?.Equals("INVALID", StringComparison.OrdinalIgnoreCase) == true
        || status?.Equals("failed", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDiagnosticDispatch(PipelineDispatch dispatch)
        => dispatch.Type.Equals("halt", StringComparison.OrdinalIgnoreCase)
        || dispatch.Message.Contains("halting", StringComparison.OrdinalIgnoreCase)
        || dispatch.Message.Contains("max retries exhausted", StringComparison.OrdinalIgnoreCase)
        || dispatch.Message.Contains("returned '", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, JsonElement>? TryReadSummary(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var matches = FencedJsonRegex.Matches(output);
        if (matches.Count == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(matches[^1].Groups["json"].Value);
            return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadSummaryText(Dictionary<string, JsonElement>? summary, params string[] keys)
    {
        if (summary is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!summary.TryGetValue(key, out var value))
            {
                continue;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Select(ToDisplayText).Where(item => !string.IsNullOrWhiteSpace(item))),
                JsonValueKind.Object => value.ToString(),
                _ => value.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadSummaryList(Dictionary<string, JsonElement>? summary, params string[] keys)
    {
        if (summary is null)
        {
            return [];
        }

        foreach (var key in keys)
        {
            if (!summary.TryGetValue(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(ToDisplayText)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!)
                    .ToArray();
            }

            var text = ToDisplayText(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return [text];
            }
        }

        return [];
    }

    private static string? ToDisplayText(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();

    private static IReadOnlyList<string> BuildCorrectiveActions(string reason, string? stageName, Dictionary<string, JsonElement>? summary)
    {
        var structuredActions = ReadSummaryList(summary, "corrective_actions", "required_actions", "next_steps", "action_items");
        if (structuredActions.Count > 0)
        {
            return structuredActions;
        }

        if (reason.Contains("approve-all", StringComparison.OrdinalIgnoreCase))
        {
            return [
                "Enable approval for trusted dashboard runs in Cyberpilot configuration.",
                "Continue or restart the run after approval is enabled."
            ];
        }

        if (reason.Contains("model unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return [
                "Select a model that is available to the current Copilot account.",
                "Start a new run with the available model."
            ];
        }

        if (reason.Contains("No fenced JSON result block", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("Malformed JSON", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("missing required property", StringComparison.OrdinalIgnoreCase))
        {
            return [
                "Check the agent transcript for the final response shape.",
                "Update the agent prompt or rerun the stage so it ends with the required fenced JSON result block."
            ];
        }

        if (string.Equals(stageName, "review", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("Review", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("changes_requested", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("max retries", StringComparison.OrdinalIgnoreCase))
        {
            return [
                "Open the linked PR and the latest Review card output to identify the blocking findings.",
                "Address the requested changes on the existing branch.",
                "Use Rework from Review to send those findings back to implementation, then Cyberpilot will return to review."
            ];
        }

        if (string.Equals(stageName, "triage", StringComparison.OrdinalIgnoreCase))
        {
            return [
                "Clarify the GitHub issue until triage can produce a GO or DUPLICATE decision.",
                "Use Continue or reset the mission after updating the issue."
            ];
        }

        return [
            "Read the stopped stage output for the blocking condition.",
            "Correct the issue, branch, PR, or stage handoff artifact called out by that output.",
            "Use Continue to retry from the stopped stage, or Reset to rerun from a clean issue state."
        ];
    }

    private static string? BuildEvidence(PipelineDispatch? dispatch, PipelineStageLog? log, Dictionary<string, JsonElement>? summary)
    {
        var status = ReadSummaryText(summary, "status", "decision");
        if (!string.IsNullOrWhiteSpace(status) && log is not null)
        {
            return $"{DisplayStage(log.StageName)} result: {status}.";
        }

        if (dispatch is not null)
        {
            return $"Dispatch: {dispatch.Message}";
        }

        return log is null ? null : $"{DisplayStage(log.StageName)} status: {log.Status}.";
    }

    private static string BuildTitle(string status, string? stageName)
    {
        var stage = string.IsNullOrWhiteSpace(stageName) ? "pipeline" : DisplayStage(stageName);
        return status == "Failed" ? $"{stage} failed" : $"{stage} stopped";
    }

    private static string DisplayStage(string stageName)
        => char.ToUpperInvariant(stageName[0]) + stageName[1..];

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

/// <summary>
/// Displays one themed AI-SDLC guide page for a specific mode.
/// </summary>
/// <param name="Mode">The pipeline mode key.</param>
/// <param name="Title">The branded guide title.</param>
/// <param name="Summary">A short mode summary line.</param>
/// <param name="HtmlContent">Rendered markdown HTML for the guide body.</param>
/// <param name="SourceFileName">The source markdown file name.</param>
public sealed record PipelineGuideViewModel(
    string Mode,
    string Title,
    string Summary,
    string HtmlContent,
    string SourceFileName);

/// <summary>
/// Captures a request to retry a specific pipeline stage.
/// </summary>
public sealed class RetryStageRequest
{
    /// <summary>Gets or sets the stage name to retry (e.g., "plan", "implement", "review").</summary>
    [Required]
    [StringLength(80)]
    public string StageName { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional model override for this retry attempt.</summary>
    [StringLength(120)]
    public string? Model { get; set; }

    /// <summary>Gets or sets an optional stage timeout override in minutes for this retry attempt.</summary>
    [Range(1, 120)]
    public int? StageTimeoutMinutes { get; set; }

    /// <summary>Gets or sets an optional operator note explaining why the stage is being retried.</summary>
    [StringLength(500)]
    public string? RetryReason { get; set; }
}

/// <summary>
/// Captures a request to start Cyberpilot from the web UI.
/// </summary>
public sealed class PipelineStartRequest
{
    /// <summary>Gets or sets the issue number to run.</summary>
    [Range(1, int.MaxValue)]
    public int IssueNumber { get; set; }

    /// <summary>Gets or sets the repository URL or owner/name value.</summary>
    [Required]
    [StringLength(300)]
    public string Repository { get; set; } = string.Empty;

    /// <summary>Gets or sets the short-lived credential connection identifier.</summary>
    [StringLength(80)]
    public string? ConnectionId { get; set; }

    /// <summary>Gets or sets the Copilot model.</summary>
    [Required]
    [StringLength(120)]
    public string Model { get; set; } = "claude-sonnet-4.6";

    /// <summary>Gets or sets whether delivery should be skipped.</summary>
    public bool SkipDeliver { get; set; }

    /// <summary>Gets or sets the per-stage timeout in minutes.</summary>
    [Range(1, 120)]
    public int StageTimeoutMinutes { get; set; } = 20;

    /// <summary>Gets or sets whether missing docs may be tolerated.</summary>
    public bool AllowMissingDocs { get; set; }
}
