using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cyberpilot.Web.Controllers;

public partial class PipelinesController
{
    /// <summary>
    /// Requeues a terminal pipeline run so processing can continue from the run details page.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/Continue")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Continue(string id)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();

        if (run.Status is "Queued" or "Running" or "Pausing")
            return RedirectWithError(nameof(Details), new { id }, "This run is already active.");

        if (run.Status is not ("Failed" or "Stopped" or "Paused" or "Cancelled"))
            return RedirectWithError(nameof(Details), new { id }, "This run cannot be continued from its current status.");

        var approvalError = await CheckApprovalBlockersAsync(run.Id, id);
        if (approvalError is not null) return approvalError;

        var conflictError = await CheckConflictingRunAsync(run, id);
        if (conflictError is not null) return conflictError;

        var (repoRoot, token) = ResolveRepoConfig(run.Repository);

        run.Status = "Queued";
        run.CompletedAt = null;
        run.Error = null;
        run.TriggeredBy = User.Identity?.Name ?? run.TriggeredBy;
        await _dbContext.SaveChangesAsync();

        await EnqueueRunAsync(run, repoRoot, token, "Review feedback routed back to implementation.");

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Sends a blocked review run back to implementation so review feedback can be addressed on the existing PR branch.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/ReworkFromReview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReworkFromReview(string id)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();

        if (run.Status is "Queued" or "Running" or "Pausing")
            return RedirectWithError(nameof(Details), new { id }, "This run is already active.");

        if (!await PipelineRunPredicates.IsReviewReworkCandidateAsync(run, _dbContext))
            return RedirectWithError(nameof(Details), new { id }, "Rework from Review is only available for stopped or failed review runs.");

        var conflictError = await CheckConflictingRunAsync(run, id);
        if (conflictError is not null) return conflictError;

        var (repoRoot, token) = ResolveRepoConfig(run.Repository);

        run.Status = "Queued";
        run.CompletedAt = null;
        run.Error = null;
        run.CurrentStage = "implement";
        run.TriggeredBy = User.Identity?.Name ?? run.TriggeredBy;
        await _dbContext.SaveChangesAsync();

        await EnqueueRunAsync(run, repoRoot, token, "Review feedback routed back to implementation.");

        TempData["PipelineNotice"] = "Review feedback routed back to implementation. Cyberpilot will update the existing PR branch, then return to review.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Requeues a terminal pipeline run from a specific stage chosen by the operator.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <param name="request">The stage retry request containing the stage name and optional overrides.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/RetryStage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryStage(string id, RetryStageRequest request)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();
        if (!ModelState.IsValid) return RedirectToAction(nameof(Details), new { id });

        if (run.Status is "Queued" or "Running" or "Pausing")
            return RedirectWithError(nameof(Details), new { id }, "This run is already active.");

        if (run.Status is not ("Failed" or "Stopped" or "Paused" or "Cancelled"))
            return RedirectWithError(nameof(Details), new { id }, "This run cannot be retried from its current status.");

        var conflictError = await CheckConflictingRunAsync(run, id);
        if (conflictError is not null) return conflictError;

        if (!PipelineRunDetailsViewModel.ValidStageNames.Any(s => s.Equals(request.StageName, StringComparison.OrdinalIgnoreCase)))
            return RedirectWithError(nameof(Details), new { id }, $"'{request.StageName}' is not a recognized pipeline stage.");

        // ToLower() is used here because EF Core cannot translate StringComparison enum overloads to SQL.
        var stageLogCount = await _dbContext.PipelineStageLogs.CountAsync(log =>
            log.RunId == id && log.StageName.ToLower() == request.StageName.ToLower());
        if (stageLogCount >= _options.MaxStageRetries)
            return RedirectWithError(nameof(Details), new { id }, $"Maximum retry attempts reached for the '{request.StageName}' stage.");

        if (!string.IsNullOrWhiteSpace(request.Model)) run.Model = request.Model;
        if (request.StageTimeoutMinutes.HasValue) run.StageTimeoutMinutes = request.StageTimeoutMinutes.Value;

        var (repoRoot, token) = ResolveRepoConfig(run.Repository);

        run.Status = "Queued";
        run.CurrentStage = request.StageName;
        run.CompletedAt = null;
        run.Error = null;
        run.TriggeredBy = User.Identity?.Name ?? run.TriggeredBy;
        await _dbContext.SaveChangesAsync();

        await EnqueueRunAsync(run, repoRoot, token, request.RetryReason?.Trim());

        TempData["PipelineNotice"] = $"Stage '{request.StageName}' queued for retry.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Delivers a completed run by re-queuing it with SkipDeliver disabled so the deliver stage runs.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/DeliverNow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeliverNow(string id)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();

        if (run.Status != "Completed" || !run.SkipDeliver)
            return RedirectWithError(nameof(Details), new { id }, "Deliver is only available for completed runs that skipped delivery.");

        var (repoRoot, token) = ResolveRepoConfig(run.Repository);

        run.SkipDeliver = false;
        run.Status = "Queued";
        run.CompletedAt = null;
        run.Error = null;
        run.CurrentStage = "deliver";
        run.TriggeredBy = User.Identity?.Name ?? run.TriggeredBy;
        await _dbContext.SaveChangesAsync();

        await EnqueueRunAsync(run, repoRoot, token);

        TempData["PipelineNotice"] = "Delivery stage initiated — the PR will be merged.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Resets a run's issue for replay by removing SDK stage labels, Cyberpilot comments, and SDK issue branches.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>A redirect to the run details page, or the dashboard when reset succeeds.</returns>
    [HttpPost("{id}/ResetMission")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetMission(string id)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();

        if (run.Status is "Queued" or "Running" or "Pausing")
            return RedirectWithError(nameof(Details), new { id }, "Cancel or finish the active run before resetting the mission.");

        if (PipelineRunPredicates.IsDeliveredRun(run))
            return RedirectWithError(nameof(Details), new { id }, "Reset Mission is not available after the code has been delivered.");

        var (repoRoot, token) = ResolveRepoConfig(run.Repository);

        var issueClient = string.IsNullOrWhiteSpace(token)
            ? _issueClient
            : _issueClientFactory.Create(run.Repository, token);
        try
        {
            var issue = await issueClient.GetIssueAsync(run.IssueNumber, HttpContext.RequestAborted);
            await GitHubIssueHelper.ResetIssueLabelsAsync(issueClient, run.IssueNumber, HttpContext.RequestAborted);
            var deletedComments = await GitHubIssueHelper.DeleteAgentCommentsAsync(issueClient, run.IssueNumber, HttpContext.RequestAborted);
            var branchName = run.BranchName;
            if (string.IsNullOrWhiteSpace(branchName))
            {
                branchName = BranchProvisioner.CreateBranchName(run.IssueNumber, issue?.Title ?? $"issue-{run.IssueNumber}");
            }

            var branchDeleted = await GitHelper.DeleteIssueBranchAsync(repoRoot, branchName, HttpContext.RequestAborted);

            TempData["PipelineNotice"] = branchDeleted
                ? $"Mission reset. Removed {deletedComments} agent comment(s), cleared SDK stage labels, and deleted branch {branchName}."
                : $"Mission reset. Removed {deletedComments} agent comment(s) and cleared SDK stage labels. Branch {branchName} was not found or could not be deleted.";

            _dbContext.PipelineRuns.Remove(run);
            await _dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _logger.LogError(ex, "Failed to reset mission {RunId} for issue {IssueNumber} in {Repository}.", run.Id, run.IssueNumber, run.Repository);
            TempData["PipelineError"] = $"Mission reset failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Marks a queued or running pipeline run as cancellation requested.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string id)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();

        if (run.Status is "Queued" or "Running" or "Pausing")
        {
            run.Status = "Cancelled";
            run.CompletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Requests a pause of a running pipeline run. The runner will pause between stages.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("{id}/Pause")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(string id)
    {
        var run = await _dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == id);
        if (run is null) return NotFound();

        if (run.Status == "Running")
        {
            run.Status = "Pausing";
            await _dbContext.SaveChangesAsync();
            TempData["PipelineNotice"] = "Pause requested — the pipeline will pause after the current stage completes.";
        }
        else
        {
            TempData["PipelineError"] = "Only running pipelines can be paused.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
