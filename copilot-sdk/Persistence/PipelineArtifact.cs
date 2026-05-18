using System.ComponentModel.DataAnnotations;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists a structured artifact produced by a Cyberpilot pipeline stage.
/// </summary>
public sealed class PipelineArtifact
{
    /// <summary>Gets or sets the artifact identifier.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Gets or sets the owning run identifier.</summary>
    [Required]
    [StringLength(64)]
    public string RunId { get; set; } = string.Empty;

    /// <summary>Gets or sets the related stage log identifier, when available.</summary>
    public int? StageLogId { get; set; }

    /// <summary>Gets or sets the stage that produced the artifact.</summary>
    [Required]
    [StringLength(80)]
    public string StageName { get; set; } = string.Empty;

    /// <summary>Gets or sets the artifact name or type.</summary>
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the artifact value or summary, when available.</summary>
    [StringLength(4000)]
    public string? Value { get; set; }

    /// <summary>Gets or sets a URI pointing to the artifact, when available.</summary>
    [StringLength(1000)]
    public string? Uri { get; set; }

    /// <summary>Gets or sets the artifact media type, when available.</summary>
    [StringLength(200)]
    public string? MediaType { get; set; }

    /// <summary>Gets or sets the structured result contract version that produced the artifact.</summary>
    [StringLength(40)]
    public string? ContractVersion { get; set; }

    /// <summary>Gets or sets the source that captured this artifact.</summary>
    [Required]
    [StringLength(80)]
    public string Source { get; set; } = "stage-result";

    /// <summary>Gets or sets when this artifact row was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the owning run.</summary>
    public PipelineRun? Run { get; set; }

    /// <summary>Gets or sets the related stage log, when available.</summary>
    public PipelineStageLog? StageLog { get; set; }

    /// <summary>
    /// Creates artifact rows from a structured stage result.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="stageName">The stage that produced the result.</param>
    /// <param name="stageLog">The related stage log, when available.</param>
    /// <param name="result">The structured stage result.</param>
    /// <returns>Artifact rows for the structured stage result.</returns>
    public static IReadOnlyList<PipelineArtifact> FromStageResult(string runId, string stageName, PipelineStageLog? stageLog, StageResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        ArgumentNullException.ThrowIfNull(result);

        return result.Artifacts?
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.Name))
            .Select(artifact => new PipelineArtifact
            {
                RunId = runId,
                StageLogId = stageLog?.Id,
                StageName = stageName,
                Name = artifact.Name.Trim(),
                Value = string.IsNullOrWhiteSpace(artifact.Value) ? null : artifact.Value.Trim(),
                Uri = string.IsNullOrWhiteSpace(artifact.Uri) ? null : artifact.Uri.Trim(),
                MediaType = string.IsNullOrWhiteSpace(artifact.MediaType) ? null : artifact.MediaType.Trim(),
                ContractVersion = string.IsNullOrWhiteSpace(result.ContractVersion) ? PipelineDefinitionDefaults.ContractVersion : result.ContractVersion,
            })
            .ToArray() ?? [];
    }
}