using System.ComponentModel.DataAnnotations;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists the lifecycle of one Cyberpilot pipeline run.
/// </summary>
public sealed class PipelineRun
{
    /// <summary>Gets or sets the run identifier.</summary>
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets the GitHub issue number.</summary>
    [Range(1, int.MaxValue)]
    public int IssueNumber { get; set; }

    /// <summary>Gets or sets the repository in owner/name form.</summary>
    [Required]
    [StringLength(200)]
    public string Repository { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable issue branch name.</summary>
    [StringLength(200)]
    public string? BranchName { get; set; }

    /// <summary>Gets or sets the Copilot model identifier.</summary>
    [Required]
    [StringLength(120)]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the run status.</summary>
    [Required]
    [StringLength(40)]
    public string Status { get; set; } = "Queued";

    /// <summary>Gets or sets the current pipeline stage.</summary>
    [StringLength(80)]
    public string? CurrentStage { get; set; }

    /// <summary>Gets or sets the pull request URL when one is created.</summary>
    [StringLength(500)]
    public string? PrUrl { get; set; }

    /// <summary>Gets or sets whether this run executes via remote GitHub Actions workflows.</summary>
    public bool IsRemote { get; set; }

    /// <summary>Gets or sets the target repository in owner/name form for remote runs.</summary>
    [StringLength(200)]
    public string? TargetRepository { get; set; }

    /// <summary>Gets or sets the GitHub Actions workflow run ID for remote runs.</summary>
    public long? GitHubActionsRunId { get; set; }

    /// <summary>Gets or sets the direct URL to the GitHub issue.</summary>
    [StringLength(500)]
    public string? IssueUrl { get; set; }

    /// <summary>Gets or sets the issue title at the time the run was created.</summary>
    [StringLength(500)]
    public string? IssueTitle { get; set; }

    /// <summary>Gets or sets the last error, when the run fails.</summary>
    [StringLength(2000)]
    public string? Error { get; set; }

    /// <summary>Gets or sets the local execution path used by the run.</summary>
    [StringLength(500)]
    public string? WorktreePath { get; set; }

    /// <summary>Gets or sets when the run was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets when the run completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Gets or sets the authenticated user who triggered the run.</summary>
    [StringLength(200)]
    public string? TriggeredBy { get; set; }

    /// <summary>Gets or sets whether delivery should be skipped.</summary>
    public bool SkipDeliver { get; set; }

    /// <summary>Gets or sets the per-stage timeout in minutes.</summary>
    public double StageTimeoutMinutes { get; set; }

    /// <summary>Gets or sets whether missing docs may be tolerated.</summary>
    public bool AllowMissingDocs { get; set; }

    /// <summary>Gets or sets the pipeline definition used for this run.</summary>
    [StringLength(120)]
    public string? PipelineDefinitionName { get; set; }

    /// <summary>Gets or sets the pipeline definition version used for this run.</summary>
    [StringLength(40)]
    public string? PipelineDefinitionVersion { get; set; }

    /// <summary>Gets or sets the policy profile selected for this run.</summary>
    [StringLength(80)]
    public string? PolicyProfileName { get; set; }

    /// <summary>Gets or sets the stage result contract version selected for this run.</summary>
    [StringLength(40)]
    public string? ContractVersion { get; set; }

    /// <summary>Gets or sets the stage logs for this run.</summary>
    public ICollection<PipelineStageLog> StageLogs { get; set; } = [];

    /// <summary>Gets or sets the orchestrator dispatch events for this run.</summary>
    public ICollection<PipelineDispatch> Dispatches { get; set; } = [];

    /// <summary>Gets or sets the approval requests for this run.</summary>
    public ICollection<PipelineApproval> Approvals { get; set; } = [];

    /// <summary>Gets or sets structured evidence rows for this run.</summary>
    public ICollection<PipelineEvidence> Evidence { get; set; } = [];
}
