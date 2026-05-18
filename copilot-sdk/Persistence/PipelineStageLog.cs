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

    /// <summary>Gets or sets the model that actually reported usage for this stage.</summary>
    [StringLength(120)]
    public string? Model { get; set; }

    /// <summary>Gets or sets the model configured for this stage before fallback.</summary>
    [StringLength(120)]
    public string? ConfiguredModel { get; set; }

    /// <summary>Gets or sets the model selected for this stage after availability checks.</summary>
    [StringLength(120)]
    public string? SelectedModel { get; set; }

    /// <summary>Gets or sets the fallback model used for this stage, when any.</summary>
    [StringLength(120)]
    public string? FallbackModel { get; set; }

    /// <summary>Gets or sets why the fallback model was selected, when any.</summary>
    [StringLength(1000)]
    public string? FallbackReason { get; set; }

    /// <summary>Gets or sets the number of cache read tokens reported for this stage.</summary>
    public int? CacheReadTokens { get; set; }

    /// <summary>Gets or sets the number of cache write tokens reported for this stage.</summary>
    public int? CacheWriteTokens { get; set; }

    /// <summary>Gets or sets the number of reasoning tokens reported for this stage.</summary>
    public int? ReasoningTokens { get; set; }

    /// <summary>Gets or sets the premium request cost or multiplier reported by the SDK.</summary>
    public double? PremiumRequestCost { get; set; }

    /// <summary>Gets or sets the accumulated model API duration in milliseconds.</summary>
    public double? DurationMs { get; set; }

    /// <summary>Gets or sets the number of assistant turns observed for this stage.</summary>
    public int? TurnCount { get; set; }

    /// <summary>Gets or sets the number of tool calls started for this stage.</summary>
    public int? ToolCallCount { get; set; }

    /// <summary>Gets or sets the number of tool calls that completed unsuccessfully.</summary>
    public int? FailedToolCallCount { get; set; }

    /// <summary>Gets or sets the number of session error events observed for this stage.</summary>
    public int? SessionErrorCount { get; set; }

    /// <summary>Gets or sets whether the session reached an idle state.</summary>
    public bool? ReachedIdle { get; set; }

    /// <summary>Gets or sets whether the idle session reported an abort.</summary>
    public bool? WasAborted { get; set; }

    /// <summary>Gets or sets provider call identifiers observed for this stage.</summary>
    [StringLength(2000)]
    public string? ProviderCallIds { get; set; }

    /// <summary>Gets or sets API call identifiers observed for this stage.</summary>
    [StringLength(2000)]
    public string? ApiCallIds { get; set; }

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

    /// <summary>Gets or sets structured evidence rows related to this stage log.</summary>
    public ICollection<PipelineEvidence> Evidence { get; set; } = [];

    /// <summary>Gets or sets structured artifact rows related to this stage log.</summary>
    public ICollection<PipelineArtifact> Artifacts { get; set; } = [];

    /// <summary>Gets or sets tool failure records related to this stage log.</summary>
    public ICollection<PipelineToolFailure> ToolFailures { get; set; } = [];
}
