using System.ComponentModel.DataAnnotations;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists one orchestrator-level dispatch event for a pipeline run.
/// </summary>
public sealed class PipelineDispatch
{
    /// <summary>Gets or sets the auto-generated row identifier.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Gets or sets the parent run identifier.</summary>
    [Required]
    [StringLength(64)]
    public string RunId { get; set; } = string.Empty;

    /// <summary>Gets or sets the dispatch category (see <see cref="DispatchType"/>).</summary>
    [Required]
    [StringLength(40)]
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable event description.</summary>
    [Required]
    [StringLength(500)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets when the dispatch was recorded (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
