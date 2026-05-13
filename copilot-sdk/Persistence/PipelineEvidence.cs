using System.ComponentModel.DataAnnotations;
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

        return rows;
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
