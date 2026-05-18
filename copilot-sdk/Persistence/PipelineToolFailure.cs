using System.ComponentModel.DataAnnotations;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists details for a single failed tool call observed during a Cyberpilot pipeline stage.
/// </summary>
public sealed class PipelineToolFailure
{
    /// <summary>Gets or sets the failure record identifier.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Gets or sets the owning run identifier.</summary>
    [Required]
    [StringLength(64)]
    public string RunId { get; set; } = string.Empty;

    /// <summary>Gets or sets the related stage log identifier, when available.</summary>
    public int? StageLogId { get; set; }

    /// <summary>Gets or sets the stage that produced the failure.</summary>
    [Required]
    [StringLength(80)]
    public string StageName { get; set; } = string.Empty;

    /// <summary>Gets or sets the SDK tool call identifier.</summary>
    [StringLength(200)]
    public string? ToolCallId { get; set; }

    /// <summary>Gets or sets the name of the tool that failed, when known.</summary>
    [StringLength(200)]
    public string? ToolName { get; set; }

    /// <summary>Gets or sets the machine-readable error code reported by the SDK, when available.</summary>
    [StringLength(200)]
    public string? ErrorCode { get; set; }

    /// <summary>Gets or sets the human-readable error message reported by the SDK, when available.</summary>
    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets when this failure record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the owning run.</summary>
    public PipelineRun? Run { get; set; }

    /// <summary>Gets or sets the related stage log, when available.</summary>
    public PipelineStageLog? StageLog { get; set; }

    /// <summary>
    /// Creates tool failure rows from a structured stage result.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="stageName">The stage that produced the result.</param>
    /// <param name="stageLog">The related stage log, when available.</param>
    /// <param name="result">The structured stage result.</param>
    /// <returns>Tool failure rows for each failed tool call recorded in the result metrics.</returns>
    public static IReadOnlyList<PipelineToolFailure> FromStageResult(string runId, string stageName, PipelineStageLog? stageLog, StageResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        ArgumentNullException.ThrowIfNull(result);

        return result.Metrics?.FailedToolCalls?
            .Select(failure => new PipelineToolFailure
            {
                RunId = runId,
                StageLogId = stageLog?.Id,
                StageName = stageName,
                ToolCallId = string.IsNullOrWhiteSpace(failure.ToolCallId) ? null : failure.ToolCallId,
                ToolName = string.IsNullOrWhiteSpace(failure.ToolName) ? null : failure.ToolName,
                ErrorCode = string.IsNullOrWhiteSpace(failure.ErrorCode) ? null : failure.ErrorCode,
                ErrorMessage = string.IsNullOrWhiteSpace(failure.ErrorMessage) ? null : failure.ErrorMessage.Length > 2000
                    ? string.Concat(failure.ErrorMessage.AsSpan(0, 1997), "...")
                    : failure.ErrorMessage,
            })
            .ToArray() ?? [];
    }
}
