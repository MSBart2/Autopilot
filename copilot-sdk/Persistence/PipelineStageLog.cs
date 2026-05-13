using System.ComponentModel.DataAnnotations;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists stage-level output for an Cyberpilot pipeline run.
/// </summary>
public sealed class PipelineStageLog
{
    /// <summary>Gets or sets the log identifier.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Gets or sets the owning run identifier.</summary>
    [Required]
    [StringLength(64)]
    public string RunId { get; set; } = string.Empty;

    /// <summary>Gets or sets the stage name.</summary>
    [Required]
    [StringLength(80)]
    public string StageName { get; set; } = string.Empty;

    /// <summary>Gets or sets the stage status.</summary>
    [Required]
    [StringLength(40)]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets buffered output for the stage.</summary>
    public string? Output { get; set; }

    /// <summary>Gets or sets when the stage started.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets when the stage completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Gets or sets the number of input tokens consumed by this stage.</summary>
    public int? InputTokens { get; set; }

    /// <summary>Gets or sets the number of output tokens produced by this stage.</summary>
    public int? OutputTokens { get; set; }

    /// <summary>Gets or sets the estimated USD cost for this stage based on model pricing.</summary>
    public decimal? EstimatedCostUsd { get; set; }

    /// <summary>Gets or sets the retry attempt number for this stage log (0 = first attempt).</summary>
    public int? RetryCount { get; set; }

    /// <summary>Gets or sets the serialized structured stage result.</summary>
    public string? StageResultJson { get; set; }

    /// <summary>Gets or sets the structured stage result contract version.</summary>
    [StringLength(40)]
    public string? StageResultContractVersion { get; set; }

    /// <summary>Gets or sets the operator-provided reason for retrying this stage.</summary>
    [StringLength(500)]
    public string? RetryReason { get; set; }

    /// <summary>Gets or sets the owning run.</summary>
    public PipelineRun? Run { get; set; }
}
