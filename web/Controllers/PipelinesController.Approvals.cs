using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cyberpilot.Web.Controllers;

public partial class PipelinesController
{
    /// <summary>
    /// Approves a pending human approval request for a pipeline run.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <param name="approvalId">The approval identifier.</param>
    /// <param name="request">The optional decision note.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/Approvals/{approvalId}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveApproval(string id, string approvalId, PipelineApprovalDecisionRequest request)
    {
        return await DecideApprovalAsync(id, approvalId, request, "Approved");
    }

    /// <summary>
    /// Rejects a pending human approval request for a pipeline run.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <param name="approvalId">The approval identifier.</param>
    /// <param name="request">The optional decision note.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/Approvals/{approvalId}/Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectApproval(string id, string approvalId, PipelineApprovalDecisionRequest request)
    {
        return await DecideApprovalAsync(id, approvalId, request, "Rejected");
    }

    /// <summary>
    /// Resumes a run after a human approval request has been approved.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <param name="approvalId">The approval identifier.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/Approvals/{approvalId}/Resume")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResumeApproval(string id, string approvalId)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();

        var approval = await _dbContext.PipelineApprovals.FirstOrDefaultAsync(item => item.Id == approvalId && item.RunId == id);
        if (approval is null) return NotFound();

        if (run.Status is "Queued" or "Running" or "Pausing")
            return RedirectWithError(nameof(Details), new { id }, "This run is already active.");

        if (PipelineRunPredicates.IsDeliveredRun(run))
            return RedirectWithError(nameof(Details), new { id }, "Delivered runs cannot be altered.");

        if (run.Status is not ("Failed" or "Stopped" or "Paused" or "Cancelled"))
            return RedirectWithError(nameof(Details), new { id }, "This run cannot be resumed from its current status.");

        if (!approval.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            return RedirectWithError(nameof(Details), new { id }, "Only approved approval requests can resume a run.");

        if (!PipelineRunDetailsViewModel.ValidStageNames.Any(stage => stage.Equals(approval.ResumeStageName, StringComparison.OrdinalIgnoreCase)))
            return RedirectWithError(nameof(Details), new { id }, $"'{approval.ResumeStageName}' is not a recognized approval resume stage.");

        var conflictError = await CheckConflictingRunAsync(run, id);
        if (conflictError is not null) return conflictError;

        var (repoRoot, token) = ResolveRepoConfig(run.Repository);

        run.Status = "Queued";
        run.CurrentStage = approval.ResumeStageName;
        run.CompletedAt = null;
        run.Error = null;
        run.TriggeredBy = User.Identity?.Name ?? run.TriggeredBy;
        await _dbContext.SaveChangesAsync();

        await EnqueueRunAsync(run, repoRoot, token, $"Approval '{approval.Id}' approved; resuming at {approval.ResumeStageName}.");

        TempData["PipelineNotice"] = $"Approval accepted. Cyberpilot will resume at {approval.ResumeStageName}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> DecideApprovalAsync(string id, string approvalId, PipelineApprovalDecisionRequest request, string decision)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();

        var approval = await _dbContext.PipelineApprovals.FirstOrDefaultAsync(item => item.Id == approvalId && item.RunId == id);
        if (approval is null) return NotFound();

        if (!ModelState.IsValid)
        {
            TempData["PipelineError"] = "Approval decision note is too long.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (run.Status == "Completed" && !run.SkipDeliver)
        {
            TempData["PipelineError"] = "Delivered runs cannot be altered.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!approval.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["PipelineError"] = "This approval request has already been decided.";
            return RedirectToAction(nameof(Details), new { id });
        }

        approval.Status = decision;
        approval.DecidedBy = User.Identity?.Name ?? "operator";
        approval.DecisionReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        approval.DecidedAt = DateTime.UtcNow;

        if (decision == nameof(ApprovalStatus.Rejected))
        {
            run.Status = "Stopped";
            run.CurrentStage = approval.StageName;
            run.CompletedAt = DateTime.UtcNow;
            run.Error = string.IsNullOrWhiteSpace(approval.DecisionReason)
                ? $"Approval '{approval.Id}' was rejected after {approval.StageName}."
                : $"Approval '{approval.Id}' was rejected after {approval.StageName}: {approval.DecisionReason}";
        }

        _dbContext.PipelineEvidence.Add(PipelineEvidence.FromApprovalDecision(approval));
        await _dbContext.SaveChangesAsync();

        TempData["PipelineNotice"] = decision == nameof(ApprovalStatus.Approved)
            ? "Approval recorded. Use Resume from the approval card to continue this run."
            : "Approval rejection recorded. Address the rejection with a targeted retry or rework before continuing.";

        return RedirectToAction(nameof(Details), new { id });
    }
}
