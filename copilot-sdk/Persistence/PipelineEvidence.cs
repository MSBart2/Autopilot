using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists structured evidence captured during a Cyberpilot pipeline run.
/// </summary>
public sealed class PipelineEvidence
{
    /// <summary>Gets or sets the evidence identifier.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Gets or sets the owning run identifier.</summary>
    [Required]
    [StringLength(64)]
    public string RunId { get; set; } = string.Empty;

    /// <summary>Gets or sets the related stage log identifier, when available.</summary>
    public int? StageLogId { get; set; }

    /// <summary>Gets or sets the stage that produced this evidence.</summary>
    [Required]
    [StringLength(80)]
    public string StageName { get; set; } = string.Empty;

    /// <summary>Gets or sets the evidence kind.</summary>
    [Required]
    [StringLength(80)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the evidence name.</summary>
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a concise evidence summary.</summary>
    [Required]
    [StringLength(2000)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Gets or sets a URI for evidence details, when available.</summary>
    [StringLength(1000)]
    public string? Uri { get; set; }

    /// <summary>Gets or sets the evidence media type, when available.</summary>
    [StringLength(200)]
    public string? MediaType { get; set; }

    /// <summary>Gets or sets the source that captured this evidence.</summary>
    [Required]
    [StringLength(80)]
    public string Source { get; set; } = "stage-result";

    /// <summary>Gets or sets when this evidence row was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the owning run.</summary>
    public PipelineRun? Run { get; set; }

    /// <summary>Gets or sets the related stage log, when available.</summary>
    public PipelineStageLog? StageLog { get; set; }

    /// <summary>
    /// Creates evidence ledger rows from a structured stage result.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="stageName">The stage that produced the result.</param>
    /// <param name="stageLog">The related stage log, when available.</param>
    /// <param name="result">The structured stage result.</param>
    /// <returns>Evidence rows for the structured result.</returns>
    public static IReadOnlyList<PipelineEvidence> FromStageResult(string runId, string stageName, PipelineStageLog? stageLog, StageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rows = new List<PipelineEvidence>();
        if (result.Evidence is not null)
        {
            rows.AddRange(result.Evidence.Select(item => Create(runId, stageName, stageLog, "stage-evidence", item.Name, item.Summary, item.Uri, null)));
        }

        if (result.Artifacts is not null)
        {
            rows.AddRange(result.Artifacts.Select(item => Create(runId, stageName, stageLog, "stage-artifact", item.Name, item.Value ?? item.Name, item.Uri, item.MediaType)));
        }

        if (!string.IsNullOrWhiteSpace(result.PolicyRationale))
        {
            rows.Add(Create(runId, stageName, stageLog, "policy-rationale", "policy-rationale", result.PolicyRationale.Trim(), null, null));
        }

        if (result.RequiredActions is not null)
        {
            var index = 0;
            rows.AddRange(result.RequiredActions
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Select(action => Create(runId, stageName, stageLog, "required-action", $"required-action-{++index}", action.Trim(), null, null)));
        }

        var usage = FromUsageMetrics(runId, stageName, stageLog);
        if (usage is not null)
        {
            rows.Add(usage);
        }

        return rows;
    }

    /// <summary>
    /// Creates an evidence ledger row for stage usage telemetry.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="stageName">The stage that produced the usage telemetry.</param>
    /// <param name="stageLog">The related stage log.</param>
    /// <returns>The usage evidence row, or null when no usage telemetry is available.</returns>
    public static PipelineEvidence? FromUsageMetrics(string runId, string stageName, PipelineStageLog? stageLog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);

        if (stageLog?.InputTokens is null
            && stageLog?.OutputTokens is null
            && (stageLog?.EstimatedCostUsd is null || stageLog.EstimatedCostUsd == 0m))
        {
            return null;
        }

        var parts = new List<string>();
        if (stageLog.InputTokens.HasValue)
        {
            parts.Add($"{stageLog.InputTokens.Value.ToString("N0", CultureInfo.InvariantCulture)} input tokens");
        }

        if (stageLog.OutputTokens.HasValue)
        {
            parts.Add($"{stageLog.OutputTokens.Value.ToString("N0", CultureInfo.InvariantCulture)} output tokens");
        }

        if (stageLog.EstimatedCostUsd is > 0m)
        {
            parts.Add($"estimated cost {stageLog.EstimatedCostUsd.Value.ToString("C4", CultureInfo.GetCultureInfo("en-US"))}");
        }

        var evidence = Create(runId, stageName, stageLog, "usage-metrics", "usage", $"Usage: {string.Join(", ", parts)}.", null, "text/plain");
        evidence.Source = "telemetry";
        return evidence;
    }

    /// <summary>
    /// Creates an evidence ledger row for an approval request.
    /// </summary>
    /// <param name="approval">The approval request.</param>
    /// <returns>The approval request evidence row.</returns>
    public static PipelineEvidence FromApprovalRequest(PipelineApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);

        return new PipelineEvidence
        {
            RunId = approval.RunId,
            StageName = approval.StageName,
            Kind = "approval-request",
            Name = approval.Id,
            Summary = $"Approval requested for {approval.RequestedRole}: {approval.Reason}",
            Source = "approval",
            CreatedAt = approval.CreatedAt,
        };
    }

    /// <summary>
    /// Creates an evidence ledger row for an approval decision.
    /// </summary>
    /// <param name="approval">The decided approval request.</param>
    /// <returns>The approval decision evidence row.</returns>
    public static PipelineEvidence FromApprovalDecision(PipelineApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);

        var actor = string.IsNullOrWhiteSpace(approval.DecidedBy) ? "operator" : approval.DecidedBy;
        var reason = string.IsNullOrWhiteSpace(approval.DecisionReason) ? string.Empty : $": {approval.DecisionReason}";
        return new PipelineEvidence
        {
            RunId = approval.RunId,
            StageName = approval.StageName,
            Kind = "approval-decision",
            Name = approval.Id,
            Summary = $"Approval {approval.Status.ToLowerInvariant()} by {actor}{reason}",
            Source = "approval",
            CreatedAt = approval.DecidedAt ?? DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Creates an evidence ledger row for a prepared branch.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="branchName">The prepared branch name.</param>
    /// <returns>The branch evidence row.</returns>
    public static PipelineEvidence FromBranchReady(string runId, string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        return new PipelineEvidence
        {
            RunId = runId,
            StageName = "branch",
            Kind = "branch-reference",
            Name = branchName.Trim(),
            Summary = $"Branch ready: {branchName.Trim()}",
            Source = "git",
        };
    }

    /// <summary>
    /// Creates an evidence ledger row for a pull request reference.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="prUrl">The pull request URL.</param>
    /// <returns>The pull request evidence row.</returns>
    public static PipelineEvidence FromPullRequest(string runId, string prUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prUrl);

        var trimmedUrl = prUrl.Trim();
        return new PipelineEvidence
        {
            RunId = runId,
            StageName = "implement",
            Kind = "pull-request-reference",
            Name = "pull-request",
            Summary = $"Pull request ready: {trimmedUrl}",
            Uri = trimmedUrl,
            MediaType = "text/uri-list",
            Source = "github",
        };
    }

    /// <summary>
    /// Creates an evidence ledger row for a terminal delivery dispatch event.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="type">The dispatch type.</param>
    /// <param name="message">The dispatch message.</param>
    /// <returns>A delivery evidence row, or null when the dispatch is not delivery evidence.</returns>
    public static PipelineEvidence? FromDeliveryDispatch(string runId, string type, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var trimmedType = type.Trim();
        var trimmedMessage = message.Trim();
        var name = trimmedType switch
        {
            DispatchType.Skip => "delivery-skipped",
            DispatchType.IssueClosed => "issue-closed",
            DispatchType.Routing when trimmedMessage.StartsWith("Delivery complete", StringComparison.OrdinalIgnoreCase) => "delivery-complete",
            _ => null,
        };

        if (name is null)
        {
            return null;
        }

        return new PipelineEvidence
        {
            RunId = runId,
            StageName = "deliver",
            Kind = "delivery-outcome",
            Name = name,
            Summary = trimmedMessage,
            Source = "dispatch",
        };
    }

    private static PipelineEvidence Create(
        string runId,
        string stageName,
        PipelineStageLog? stageLog,
        string kind,
        string name,
        string summary,
        string? uri,
        string? mediaType) => new()
        {
            RunId = runId,
            StageLog = stageLog,
            StageName = stageName,
            Kind = kind,
            Name = name,
            Summary = summary,
            Uri = string.IsNullOrWhiteSpace(uri) ? null : uri.Trim(),
            MediaType = string.IsNullOrWhiteSpace(mediaType) ? null : mediaType.Trim(),
        };
}
