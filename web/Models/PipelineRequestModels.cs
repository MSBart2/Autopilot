using System.ComponentModel.DataAnnotations;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Web.Models;

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

    /// <summary>Gets or sets the built-in pipeline definition selected for the run.</summary>
    [Required]
    [StringLength(80)]
    public string PipelineDefinitionName { get; set; } = PipelineDefinitionDefaults.DefinitionName;

    /// <summary>Gets or sets the built-in policy profile selected for the run.</summary>
    [Required]
    [StringLength(80)]
    public string PolicyProfileName { get; set; } = PipelineDefinitionDefaults.PolicyProfileName;

    /// <summary>Gets or sets whether delivery should be skipped.</summary>
    public bool SkipDeliver { get; set; }

    /// <summary>Gets or sets the per-stage timeout in minutes.</summary>
    [Range(1, 120)]
    public int StageTimeoutMinutes { get; set; } = 20;

    /// <summary>Gets or sets whether missing docs may be tolerated.</summary>
    public bool AllowMissingDocs { get; set; }
}

