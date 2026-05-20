using System.Text.Json;
using System.Text.RegularExpressions;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Web.Models;

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
/// <param name="Artifacts">Structured artifacts produced by the run.</param>
public sealed record PipelineRunDetailsViewModel(PipelineRun Run, IReadOnlyList<PipelineStageLog> Logs, IReadOnlyList<string> Labels, GitHubIssueSummary? Issue = null, IReadOnlyList<PipelineDispatch>? Dispatches = null, IReadOnlyList<PipelineApproval>? Approvals = null, IReadOnlyList<PipelineEvidence>? Evidence = null, IReadOnlyList<PipelineArtifact>? Artifacts = null)
{
    /// <summary>Gets the latest plan stage output formatted as a first-class review artifact.</summary>
    public PipelinePlanReviewViewModel? PlanReview => PipelinePlanReviewViewModel.Create(Run, Logs, Evidence ?? []);

    /// <summary>Gets whether this run has plan output that can be shown for deliberate review.</summary>
    public bool HasPlanReview => PlanReview is not null;

    /// <summary>Gets the latest triage stage output formatted as a standalone document.</summary>
    public PipelineTriageReviewViewModel? TriageReview => PipelineTriageReviewViewModel.Create(Run, Logs, Evidence ?? []);

    /// <summary>Gets whether this run has triage output that can be shown for deliberate review.</summary>
    public bool HasTriageReview => TriageReview is not null;

    /// <summary>Initializes a details view model without labels.</summary>
    public PipelineRunDetailsViewModel(PipelineRun run, IReadOnlyList<PipelineStageLog> logs)
        : this(run, logs, []) { }

    /// <summary>Gets the ordered list of valid pipeline stage names.</summary>
    public static IReadOnlyList<string> ValidStageNames { get; } = ["triage", "plan", "implement", "review", "docs", "summary", "deliver"];

    /// <summary>Gets stages that can be shown as standalone stage output documents.</summary>
    public static IReadOnlySet<string> StageDocumentNames { get; } = new HashSet<string>(ValidStageNames, StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Gets structured artifacts formatted for display.</summary>
    public IReadOnlyList<PipelineArtifactViewModel> ArtifactItems => (Artifacts ?? [])
        .OrderBy(artifact => StageSortOrder(artifact.StageName))
        .ThenBy(artifact => artifact.StageName)
        .ThenBy(artifact => artifact.Name)
        .ThenBy(artifact => artifact.CreatedAt)
        .Select(PipelineArtifactViewModel.FromArtifact)
        .ToArray();

    /// <summary>Gets policy-relevant evidence rows formatted for compact policy display.</summary>
    public IReadOnlyList<PipelineEvidenceViewModel> PolicyItems => EvidenceItems
        .Where(evidence => evidence.Kind is "policy-rationale" or "gate-outcome" or "required-action")
        .ToArray();

    /// <summary>Gets whether this run has a pending human approval request.</summary>
    public bool HasPendingApprovals => ApprovalItems.Any(approval => approval.IsPending);

    /// <summary>Gets whether this run has policy-relevant evidence to summarize.</summary>
    public bool HasPolicyItems => PolicyItems.Count > 0;

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

    /// <summary>Gets the total assistant turns observed across all stage logs.</summary>
    public int TotalTurnCount => Logs.Sum(l => l.TurnCount ?? 0);

    /// <summary>Gets the total tool calls observed across all stage logs.</summary>
    public int TotalToolCallCount => Logs.Sum(l => l.ToolCallCount ?? 0);

    /// <summary>Gets the total failed tool calls observed across all stage logs.</summary>
    public int TotalFailedToolCallCount => Logs.Sum(l => l.FailedToolCallCount ?? 0);

    /// <summary>Gets the total model API duration across all stage logs, or null if no duration data is available.</summary>
    public TimeSpan? TotalModelDuration => Logs.Any(l => l.DurationMs.HasValue)
        ? TimeSpan.FromMilliseconds(Logs.Sum(l => l.DurationMs ?? 0d))
        : null;

    /// <summary>Gets whether this terminal run can be requeued from its current stage.</summary>
    public bool CanContinue => Run.Status is "Failed" or "Stopped" or "Paused" or "Cancelled" or "BlockedByGate" && !HasPendingApprovals && !HasRejectedApprovals;

    /// <summary>Gets whether the run failed because the target repository working tree is dirty.</summary>
    public bool IsRepositoryCleanlinessFailure => Run.Status == "Failed"
        && (Run.Error?.Contains("repository has uncommitted changes", StringComparison.OrdinalIgnoreCase) == true
            || (Dispatches ?? []).Any(dispatch => dispatch.Type.Equals(DispatchType.Gate, StringComparison.OrdinalIgnoreCase)
                && dispatch.Message.Contains("repository-clean", StringComparison.OrdinalIgnoreCase)
                && dispatch.Message.Contains("failed", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Gets whether the run can be sent back to implementation after a blocked review.</summary>
    public bool CanReworkFromReview => !Run.IsRemote
        && Run.Status is "Failed" or "Stopped" or "BlockedByGate"
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

    /// <summary>Gets the configured model family shown in run telemetry.</summary>
    public string ModelFamilyLabel => ResolveModelFamily(Run.Model);

    /// <summary>Gets the retry attempt count for a specific stage (number of existing stage logs for that stage).</summary>
    public int GetStageRetryCount(string stageName)
        => Logs.Count(l => l.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets whether a specific stage can be retried (run is terminal, stage is known, retry count is below the cap).</summary>
    public bool CanRetryStage(string stageName, int maxStageRetries)
        => !Run.IsRemote
        && Run.Status is "Failed" or "Stopped" or "Cancelled" or "Paused" or "BlockedByGate"
        && ValidStageNames.Any(s => s.Equals(stageName, StringComparison.OrdinalIgnoreCase))
        && GetStageRetryCount(stageName) < maxStageRetries;

    private static bool IsReviewStage(string? stageName)
        => stageName?.Equals("review", StringComparison.OrdinalIgnoreCase) == true;

    private static string ResolveModelFamily(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "Unknown";
        if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) return "Claude";
        if (model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)) return "GPT";
        return model.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? model;
    }

    private static int StageSortOrder(string stageName)
    {
        if (stageName.Equals("preflight", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

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
/// Displays a captured plan as a standalone review document.
/// </summary>
/// <param name="Run">The pipeline run that produced the plan.</param>
/// <param name="Plan">The structured plan review details.</param>
/// <param name="RenderedPlanHtml">The plan full text rendered as HTML, when available.</param>
public sealed record PipelinePlanDocumentViewModel(PipelineRun Run, PipelinePlanReviewViewModel Plan, string? RenderedPlanHtml = null)
{
    /// <summary>Gets the GitHub issue URL, when available.</summary>
    public string IssueUrl => string.IsNullOrWhiteSpace(Run.IssueUrl)
        ? $"https://github.com/{Run.Repository}/issues/{Run.IssueNumber}"
        : Run.IssueUrl;
}

/// <summary>
/// Displays a captured triage report as a standalone review document.
/// </summary>
/// <param name="Run">The pipeline run that produced the triage report.</param>
/// <param name="Triage">The structured triage details.</param>
/// <param name="RenderedTriageHtml">The triage full text rendered as HTML, when available.</param>
public sealed record PipelineTriageDocumentViewModel(PipelineRun Run, PipelineTriageReviewViewModel Triage, string? RenderedTriageHtml = null)
{
    /// <summary>Gets the GitHub issue URL, when available.</summary>
    public string IssueUrl => string.IsNullOrWhiteSpace(Run.IssueUrl)
        ? $"https://github.com/{Run.Repository}/issues/{Run.IssueNumber}"
        : Run.IssueUrl;
}

/// <summary>
/// Displays a captured pipeline stage output as a standalone document.
/// </summary>
/// <param name="Run">The pipeline run that produced the stage output.</param>
/// <param name="Stage">The structured stage output details.</param>
/// <param name="RenderedOutputHtml">The stage output rendered as HTML, when available.</param>
public sealed record PipelineStageDocumentViewModel(PipelineRun Run, PipelineStageOutputViewModel Stage, string? RenderedOutputHtml = null)
{
    /// <summary>Gets the GitHub issue URL, when available.</summary>
    public string IssueUrl => string.IsNullOrWhiteSpace(Run.IssueUrl)
        ? $"https://github.com/{Run.Repository}/issues/{Run.IssueNumber}"
        : Run.IssueUrl;
}

/// <summary>
/// Displays one non-triage pipeline stage's captured output, artifacts, and evidence.
/// </summary>
/// <param name="StageName">The stage machine name.</param>
/// <param name="StageLabel">The stage display label.</param>
/// <param name="Status">The latest stage status.</param>
/// <param name="Decision">The latest stage decision.</param>
/// <param name="Summary">The most concise available stage summary.</param>
/// <param name="FullOutputText">The full captured output or transcript for detailed review.</param>
/// <param name="Artifacts">Structured artifacts produced by the stage.</param>
/// <param name="Evidence">Evidence rows produced by the stage.</param>
/// <param name="RequiredActions">Actions the operator or agent must address before continuing.</param>
/// <param name="CreatedAt">When the displayed stage log was created.</param>
/// <param name="CompletedAt">When the displayed stage log completed, when available.</param>
/// <param name="ContractVersion">The stage result contract version, when available.</param>
public sealed record PipelineStageOutputViewModel(
    string StageName,
    string StageLabel,
    string Status,
    string Decision,
    string Summary,
    string? FullOutputText,
    IReadOnlyList<PipelinePlanArtifactViewModel> Artifacts,
    IReadOnlyList<PipelineEvidenceViewModel> Evidence,
    IReadOnlyList<string> RequiredActions,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? ContractVersion)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Gets whether the stage has detailed text to display.</summary>
    public bool HasFullOutputText => !string.IsNullOrWhiteSpace(FullOutputText);

    /// <summary>Gets whether the stage has structured artifacts to display.</summary>
    public bool HasArtifacts => Artifacts.Count > 0;

    /// <summary>Gets whether the stage has supporting evidence rows.</summary>
    public bool HasEvidence => Evidence.Count > 0;

    /// <summary>Gets whether the stage has required follow-up actions.</summary>
    public bool HasRequiredActions => RequiredActions.Count > 0;

    /// <summary>
    /// Creates a stage output model from the latest persisted stage log.
    /// </summary>
    /// <param name="stageName">The stage machine name.</param>
    /// <param name="run">The owning pipeline run.</param>
    /// <param name="logs">The stage logs for the run.</param>
    /// <param name="evidence">The evidence rows for the run.</param>
    /// <returns>A stage output model, or <see langword="null" /> when no output exists.</returns>
    public static PipelineStageOutputViewModel? Create(string stageName, PipelineRun run, IReadOnlyList<PipelineStageLog> logs, IReadOnlyList<PipelineEvidence> evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(evidence);

        var stageLog = logs
            .Where(log => log.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(log.StageResultJson) || !string.IsNullOrWhiteSpace(log.Output)))
            .OrderByDescending(log => log.CompletedAt ?? log.StartedAt)
            .FirstOrDefault();
        if (stageLog is null)
        {
            return null;
        }

        var result = TryReadStageResult(stageLog.StageResultJson);
        var artifacts = BuildArtifacts(stageName, result, evidence);
        var primaryArtifact = artifacts.FirstOrDefault(artifact => artifact.HasValue && IsPrimaryArtifact(stageName, artifact.Name))
            ?? artifacts.FirstOrDefault(artifact => artifact.HasValue && IsReadableArtifact(artifact));
        var summary = FirstNonEmpty(
            primaryArtifact?.Value,
            evidence.FirstOrDefault(item => item.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase)
                && item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase))?.Summary,
            stageLog.Output,
            $"{DisplayStage(stageName)} output was captured, but no structured summary was provided.")!;

        var stageEvidence = evidence
            .Where(item => item.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase)
                && !item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase)
                && !item.Kind.Equals("usage-metrics", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.CreatedAt)
            .Select(PipelineEvidenceViewModel.FromEvidence)
            .ToArray();

        return new PipelineStageOutputViewModel(
            stageName,
            DisplayStage(stageName),
            stageLog.Status,
            result?.Decision ?? "unknown",
            NormalizeDisplayText(summary),
            NormalizeDisplayText(FirstNonEmpty(stageLog.Output, primaryArtifact?.Value) ?? string.Empty),
            artifacts,
            stageEvidence,
            result?.RequiredActions ?? [],
            stageLog.StartedAt,
            stageLog.CompletedAt,
            result?.ContractVersion ?? stageLog.StageResultContractVersion);
    }

    private static IReadOnlyList<PipelinePlanArtifactViewModel> BuildArtifacts(string stageName, StageResult? result, IReadOnlyList<PipelineEvidence> evidence)
    {
        if (result?.Artifacts is { Count: > 0 })
        {
            return result.Artifacts
                .Select(PipelinePlanArtifactViewModel.FromArtifact)
                .ToArray();
        }

        return evidence
            .Where(item => item.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase)
                && item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAt)
            .Select(PipelinePlanArtifactViewModel.FromEvidence)
            .ToArray();
    }

    private static bool IsPrimaryArtifact(string stageName, string artifactName)
        => (stageName.ToLowerInvariant(), artifactName.ToLowerInvariant()) switch
        {
            ("implement", "pull-request") => true,
            ("implement", "validation-summary") => true,
            ("review", "review-verdict") => true,
            ("docs", "verification") => true,
            ("docs", "docs-comment") => true,
            ("summary", "summary-report") => true,
            ("summary", "pr-body-summary") => true,
            ("summary", "changelog-entry") => true,
            ("deliver", "landing-report") => true,
            _ => artifactName.EndsWith("-comment", StringComparison.OrdinalIgnoreCase)
                || artifactName.EndsWith("-report", StringComparison.OrdinalIgnoreCase)
                || artifactName.EndsWith("-verdict", StringComparison.OrdinalIgnoreCase),
        };

    private static bool IsReadableArtifact(PipelinePlanArtifactViewModel artifact)
        => string.IsNullOrWhiteSpace(artifact.MediaType)
            || artifact.MediaType.Contains("text", StringComparison.OrdinalIgnoreCase)
            || artifact.MediaType.Contains("markdown", StringComparison.OrdinalIgnoreCase);

    private static StageResult? TryReadStageResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StageResult>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace("\\r\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\t", "    ", StringComparison.Ordinal);
    }

    private static string DisplayStage(string stageName)
        => string.IsNullOrWhiteSpace(stageName)
            ? "Stage"
            : char.ToUpperInvariant(stageName[0]) + stageName[1..];

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

/// <summary>
/// Displays the triage stage output as a standalone report.
/// </summary>
/// <param name="Status">The latest triage stage status.</param>
/// <param name="Decision">The latest triage stage decision.</param>
/// <param name="Summary">The most concise available triage summary.</param>
/// <param name="FullTriageText">The full triage text or transcript available for detailed review.</param>
/// <param name="Artifacts">Structured artifacts produced by the triage stage.</param>
/// <param name="Evidence">Evidence rows produced by the triage stage.</param>
/// <param name="RequiredActions">Actions the operator or agent must address before continuing.</param>
/// <param name="CreatedAt">When the displayed triage log was created.</param>
/// <param name="CompletedAt">When the displayed triage log completed, when available.</param>
/// <param name="ContractVersion">The stage result contract version, when available.</param>
public sealed record PipelineTriageReviewViewModel(
    string Status,
    string Decision,
    string Summary,
    string? FullTriageText,
    IReadOnlyList<PipelinePlanArtifactViewModel> Artifacts,
    IReadOnlyList<PipelineEvidenceViewModel> Evidence,
    IReadOnlyList<string> RequiredActions,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? ContractVersion)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Gets whether the triage report has detailed text to display.</summary>
    public bool HasFullTriageText => !string.IsNullOrWhiteSpace(FullTriageText);

    /// <summary>Gets whether the triage report has structured artifacts to display.</summary>
    public bool HasArtifacts => Artifacts.Count > 0;

    /// <summary>Gets whether the triage report has supporting evidence rows.</summary>
    public bool HasEvidence => Evidence.Count > 0;

    /// <summary>Gets whether the triage report has required follow-up actions.</summary>
    public bool HasRequiredActions => RequiredActions.Count > 0;

    /// <summary>
    /// Creates a triage review model from the latest persisted triage stage log.
    /// </summary>
    /// <param name="run">The owning pipeline run.</param>
    /// <param name="logs">The stage logs for the run.</param>
    /// <param name="evidence">The evidence rows for the run.</param>
    /// <returns>A triage review model, or <see langword="null" /> when no triage output exists.</returns>
    public static PipelineTriageReviewViewModel? Create(PipelineRun run, IReadOnlyList<PipelineStageLog> logs, IReadOnlyList<PipelineEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(evidence);

        var triageLog = logs
            .Where(log => log.StageName.Equals("triage", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(log.StageResultJson) || !string.IsNullOrWhiteSpace(log.Output)))
            .OrderByDescending(log => log.CompletedAt ?? log.StartedAt)
            .FirstOrDefault();
        if (triageLog is null)
        {
            return null;
        }

        var result = TryReadStageResult(triageLog.StageResultJson);
        var artifacts = BuildArtifacts(result, evidence);
        var triageArtifact = artifacts.FirstOrDefault(artifact => artifact.Name.Equals("triage-comment", StringComparison.OrdinalIgnoreCase));
        var summary = FirstNonEmpty(
            triageArtifact?.Value,
            evidence.FirstOrDefault(item => item.StageName.Equals("triage", StringComparison.OrdinalIgnoreCase)
                && item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase)
                && item.Name.Equals("triage-comment", StringComparison.OrdinalIgnoreCase))?.Summary,
            triageLog.Output,
            "Triage output was captured, but no structured summary was provided.")!;

        var triageEvidence = evidence
            .Where(item => item.StageName.Equals("triage", StringComparison.OrdinalIgnoreCase)
                && !item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase)
                && !item.Kind.Equals("usage-metrics", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.CreatedAt)
            .Select(PipelineEvidenceViewModel.FromEvidence)
            .ToArray();

        return new PipelineTriageReviewViewModel(
            triageLog.Status,
            result?.Decision ?? "unknown",
            NormalizeDisplayText(summary),
            NormalizeDisplayText(FirstNonEmpty(triageArtifact?.Value, triageLog.Output) ?? string.Empty),
            artifacts,
            triageEvidence,
            result?.RequiredActions ?? [],
            triageLog.StartedAt,
            triageLog.CompletedAt,
            result?.ContractVersion ?? triageLog.StageResultContractVersion);
    }

    private static IReadOnlyList<PipelinePlanArtifactViewModel> BuildArtifacts(StageResult? result, IReadOnlyList<PipelineEvidence> evidence)
    {
        if (result?.Artifacts is { Count: > 0 })
        {
            return result.Artifacts
                .Select(PipelinePlanArtifactViewModel.FromArtifact)
                .ToArray();
        }

        return evidence
            .Where(item => item.StageName.Equals("triage", StringComparison.OrdinalIgnoreCase)
                && item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAt)
            .Select(PipelinePlanArtifactViewModel.FromEvidence)
            .ToArray();
    }

    private static StageResult? TryReadStageResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StageResult>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace("\\r\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\t", "    ", StringComparison.Ordinal);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

/// <summary>
/// Displays the plan stage output as a deliberate review artifact in the Run Room.
/// </summary>
/// <param name="Status">The latest plan stage status.</param>
/// <param name="Decision">The latest plan stage decision.</param>
/// <param name="BranchName">The branch prepared by the plan stage, when available.</param>
/// <param name="Summary">The most concise available plan summary.</param>
/// <param name="FullPlanText">The full plan text or transcript available for detailed review.</param>
/// <param name="Artifacts">Structured artifacts produced by the plan stage.</param>
/// <param name="Evidence">Evidence rows produced by the plan stage.</param>
/// <param name="RequiredActions">Actions the operator or agent must address before continuing.</param>
/// <param name="CreatedAt">When the displayed plan log was created.</param>
/// <param name="CompletedAt">When the displayed plan log completed, when available.</param>
/// <param name="ContractVersion">The stage result contract version, when available.</param>
public sealed record PipelinePlanReviewViewModel(
    string Status,
    string Decision,
    string? BranchName,
    string Summary,
    string? FullPlanText,
    IReadOnlyList<PipelinePlanArtifactViewModel> Artifacts,
    IReadOnlyList<PipelineEvidenceViewModel> Evidence,
    IReadOnlyList<string> RequiredActions,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? ContractVersion)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Gets whether a branch name is available for this plan.</summary>
    public bool HasBranch => !string.IsNullOrWhiteSpace(BranchName);

    /// <summary>Gets whether the plan has detailed text to expand.</summary>
    public bool HasFullPlanText => !string.IsNullOrWhiteSpace(FullPlanText);

    /// <summary>Gets whether the plan has structured artifacts to display.</summary>
    public bool HasArtifacts => Artifacts.Count > 0;

    /// <summary>Gets whether the plan has supporting evidence rows.</summary>
    public bool HasEvidence => Evidence.Count > 0;

    /// <summary>Gets whether the plan has required follow-up actions.</summary>
    public bool HasRequiredActions => RequiredActions.Count > 0;

    /// <summary>
    /// Creates a plan review model from the latest persisted plan stage log.
    /// </summary>
    /// <param name="run">The owning pipeline run.</param>
    /// <param name="logs">The stage logs for the run.</param>
    /// <param name="evidence">The evidence rows for the run.</param>
    /// <returns>A plan review model, or <see langword="null" /> when no plan output exists.</returns>
    public static PipelinePlanReviewViewModel? Create(PipelineRun run, IReadOnlyList<PipelineStageLog> logs, IReadOnlyList<PipelineEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(evidence);

        var planLog = logs
            .Where(log => log.StageName.Equals("plan", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(log.StageResultJson) || !string.IsNullOrWhiteSpace(log.Output)))
            .OrderByDescending(log => log.CompletedAt ?? log.StartedAt)
            .FirstOrDefault();
        if (planLog is null)
        {
            return null;
        }

        var result = TryReadStageResult(planLog.StageResultJson);
        var artifacts = BuildArtifacts(result, evidence);
        var planArtifact = artifacts.FirstOrDefault(artifact => artifact.IsPlanComment);
        var branchName = FirstNonEmpty(
            artifacts.FirstOrDefault(artifact => artifact.IsBranch)?.Value,
            run.BranchName);
        var summary = FirstNonEmpty(
            planArtifact?.Value,
            evidence.FirstOrDefault(item => item.StageName.Equals("plan", StringComparison.OrdinalIgnoreCase)
                && item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase)
                && item.Name.Equals("plan-comment", StringComparison.OrdinalIgnoreCase))?.Summary,
            planLog.Output,
            "Plan output was captured, but no structured summary was provided.")!;

        var planEvidence = evidence
            .Where(item => item.StageName.Equals("plan", StringComparison.OrdinalIgnoreCase)
                && !item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase)
                && !item.Kind.Equals("usage-metrics", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.CreatedAt)
            .Select(PipelineEvidenceViewModel.FromEvidence)
            .ToArray();

        return new PipelinePlanReviewViewModel(
            planLog.Status,
            result?.Decision ?? "unknown",
            branchName,
            NormalizeDisplayText(summary),
            NormalizeDisplayText(FirstNonEmpty(planArtifact?.Value, planLog.Output) ?? string.Empty),
            artifacts,
            planEvidence,
            result?.RequiredActions ?? [],
            planLog.StartedAt,
            planLog.CompletedAt,
            result?.ContractVersion ?? planLog.StageResultContractVersion);
    }

    private static IReadOnlyList<PipelinePlanArtifactViewModel> BuildArtifacts(StageResult? result, IReadOnlyList<PipelineEvidence> evidence)
    {
        if (result?.Artifacts is { Count: > 0 })
        {
            return result.Artifacts
                .Select(PipelinePlanArtifactViewModel.FromArtifact)
                .ToArray();
        }

        return evidence
            .Where(item => item.StageName.Equals("plan", StringComparison.OrdinalIgnoreCase)
                && item.Kind.Equals("stage-artifact", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAt)
            .Select(PipelinePlanArtifactViewModel.FromEvidence)
            .ToArray();
    }

    private static StageResult? TryReadStageResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StageResult>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace("\\r\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\t", "    ", StringComparison.Ordinal);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

/// <summary>
/// Displays one structured plan artifact in the Run Room.
/// </summary>
/// <param name="Name">The artifact machine name.</param>
/// <param name="Label">The artifact display label.</param>
/// <param name="Value">The artifact value or summary, when available.</param>
/// <param name="Uri">A URI pointing to the artifact, when available.</param>
/// <param name="MediaType">The artifact media type, when available.</param>
public sealed record PipelinePlanArtifactViewModel(string Name, string Label, string? Value, string? Uri, string? MediaType)
{
    /// <summary>Gets whether this artifact represents the implementation plan comment.</summary>
    public bool IsPlanComment => Name.Equals("plan-comment", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets whether this artifact represents the prepared branch.</summary>
    public bool IsBranch => Name.Equals("branch", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets whether this artifact has displayable value text.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    /// <summary>Gets whether this artifact has a detail link.</summary>
    public bool HasUri => !string.IsNullOrWhiteSpace(Uri);

    /// <summary>Creates a display model from a structured stage artifact.</summary>
    /// <param name="artifact">The structured stage artifact.</param>
    /// <returns>The artifact display model.</returns>
    public static PipelinePlanArtifactViewModel FromArtifact(StageArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return new PipelinePlanArtifactViewModel(
            artifact.Name,
            BuildLabel(artifact.Name),
            NormalizeValue(artifact.Value),
            string.IsNullOrWhiteSpace(artifact.Uri) ? null : artifact.Uri.Trim(),
            string.IsNullOrWhiteSpace(artifact.MediaType) ? null : artifact.MediaType.Trim());
    }

    /// <summary>Creates a display model from a persisted evidence artifact row.</summary>
    /// <param name="evidence">The persisted evidence row.</param>
    /// <returns>The artifact display model.</returns>
    public static PipelinePlanArtifactViewModel FromEvidence(PipelineEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return new PipelinePlanArtifactViewModel(
            evidence.Name,
            BuildLabel(evidence.Name),
            NormalizeValue(evidence.Summary),
            string.IsNullOrWhiteSpace(evidence.Uri) ? null : evidence.Uri.Trim(),
            string.IsNullOrWhiteSpace(evidence.MediaType) ? null : evidence.MediaType.Trim());
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim()
            .Replace("\\r\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\t", "    ", StringComparison.Ordinal);
    }

    private static string BuildLabel(string name)
        => name switch
        {
            "plan-comment" => "Plan",
            "branch" => "Branch",
            _ when string.IsNullOrWhiteSpace(name) => "Artifact",
            _ => string.Join(' ', name.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])),
        };
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
        "branch-reference" => "Branch",
        "pull-request-reference" => "Pull Request",
        "usage-metrics" => "Usage",
        "delivery-outcome" => "Delivery",
        "gate-outcome" => "Gate",
        "repository-profile" => "Repository",
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
/// Formats a persisted pipeline artifact for display in the run details page.
/// </summary>
/// <param name="StageName">The stage that produced the artifact.</param>
/// <param name="Name">The artifact name.</param>
/// <param name="Value">The artifact value or summary.</param>
/// <param name="Uri">The artifact URI, when available.</param>
/// <param name="MediaType">The artifact media type, when available.</param>
/// <param name="ContractVersion">The contract version that produced the artifact, when available.</param>
/// <param name="CreatedAt">When the artifact was captured.</param>
public sealed record PipelineArtifactViewModel(
    string StageName,
    string Name,
    string? Value,
    string? Uri,
    string? MediaType,
    string? ContractVersion,
    DateTime CreatedAt)
{
    /// <summary>Gets the artifact label formatted for display.</summary>
    public string Label => Humanize(Name);

    /// <summary>Gets the producing stage formatted for display.</summary>
    public string StageLabel => Humanize(StageName);

    /// <summary>Gets whether the artifact has a display value.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    /// <summary>Gets whether the artifact has a URI.</summary>
    public bool HasUri => !string.IsNullOrWhiteSpace(Uri);

    /// <summary>Creates a view model from a persisted artifact row.</summary>
    /// <param name="artifact">The persisted artifact.</param>
    /// <returns>The formatted artifact view model.</returns>
    public static PipelineArtifactViewModel FromArtifact(PipelineArtifact artifact)
        => new(artifact.StageName, artifact.Name, artifact.Value, artifact.Uri, artifact.MediaType, artifact.ContractVersion, artifact.CreatedAt);

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";
        return string.Join(' ', value.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
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


