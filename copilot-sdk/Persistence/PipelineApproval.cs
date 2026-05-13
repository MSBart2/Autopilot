using System.ComponentModel.DataAnnotations;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Persistence;

/// <summary>
/// Persists a human approval request for a Cyberpilot pipeline run.
/// </summary>
public sealed class PipelineApproval
{
    internal static PipelineApproval FromRequest(string runId, ApprovalGateRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(request);

        var approval = new PipelineApproval
        {
            Id = request.Id,
            RunId = runId,
            IssueNumber = request.IssueNumber,
            StageName = request.StageName,
            Timing = request.Timing.ToString(),
            Reason = request.Reason,
            RequestedRole = request.RequestedRole,
            ResumeStageName = request.ResumeStageName,
            Status = request.Status.ToString(),
            CreatedAt = request.CreatedAt.UtcDateTime,
        };

        if (request.Decision is not null)
        {
            approval.DecidedBy = request.Decision.DecidedBy;
            approval.DecisionReason = request.Decision.Reason;
            approval.DecidedAt = request.Decision.DecidedAt.UtcDateTime;
        }

        return approval;
    }

    /// <summary>Gets or sets the approval identifier.</summary>
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets the owning run identifier.</summary>
    [Required]
    [StringLength(64)]
    public string RunId { get; set; } = string.Empty;

    /// <summary>Gets or sets the GitHub issue number associated with the approval.</summary>
    [Range(1, int.MaxValue)]
    public int IssueNumber { get; set; }

    /// <summary>Gets or sets the stage that requested approval.</summary>
    [Required]
    [StringLength(80)]
    public string StageName { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the approval is before or after the stage.</summary>
    [Required]
    [StringLength(40)]
    public string Timing { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable approval reason.</summary>
    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets the role requested to decide the approval.</summary>
    [Required]
    [StringLength(120)]
    public string RequestedRole { get; set; } = string.Empty;

    /// <summary>Gets or sets the stage to resume after approval.</summary>
    [Required]
    [StringLength(80)]
    public string ResumeStageName { get; set; } = string.Empty;

    /// <summary>Gets or sets the approval status.</summary>
    [Required]
    [StringLength(40)]
    public string Status { get; set; } = "Pending";

    /// <summary>Gets or sets when the approval was requested.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets who decided the approval.</summary>
    [StringLength(200)]
    public string? DecidedBy { get; set; }

    /// <summary>Gets or sets the decision reason.</summary>
    [StringLength(1000)]
    public string? DecisionReason { get; set; }

    /// <summary>Gets or sets when the approval was decided.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Gets or sets the owning run.</summary>
    public PipelineRun? Run { get; set; }
}
