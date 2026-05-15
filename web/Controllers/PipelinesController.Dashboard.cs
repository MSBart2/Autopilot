using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Cyberpilot.Web.Controllers;

public partial class PipelinesController
{
    /// <summary>
    /// Displays the Cyberpilot pipeline dashboard.
    /// </summary>
    /// <returns>The pipeline dashboard view.</returns>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var runs = await _dbContext.PipelineRuns
            .OrderByDescending(run => run.CreatedAt)
            .Take(50)
            .AsNoTracking()
            .ToArrayAsync();

        return View(PipelineIssuesViewBuilder.BuildDashboard(runs));
    }

    /// <summary>
    /// Displays details and logs for a single pipeline run.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>The details view, or NotFound when the run does not exist.</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id)
    {
        var run = await _dbContext.PipelineRuns.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (run is null)
        {
            return NotFound();
        }

        var logs = await _dbContext.PipelineStageLogs
            .Where(log => log.RunId == id)
            .OrderBy(log => log.StartedAt)
            .AsNoTracking()
            .ToArrayAsync();

        var dispatches = await _dbContext.PipelineDispatches
            .Where(d => d.RunId == id)
            .OrderBy(d => d.CreatedAt)
            .AsNoTracking()
            .ToArrayAsync();

        var approvals = await _dbContext.PipelineApprovals
            .Where(approval => approval.RunId == id)
            .OrderBy(approval => approval.CreatedAt)
            .AsNoTracking()
            .ToArrayAsync();

        var evidence = await _dbContext.PipelineEvidence
            .Where(item => item.RunId == id)
            .OrderBy(item => item.StageName)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.CreatedAt)
            .AsNoTracking()
            .ToArrayAsync();

        GitHubIssueSummary? issue = null;
        IReadOnlyList<string> labels = [];
        try
        {
            var issueClient = _configHelper.TryGetConfiguredRepository(run.Repository, out var configuredRepository)
                ? _issueClientFactory.Create(configuredRepository.Repository, configuredRepository.Token)
                : _issueClient;

            issue = await _cache.GetOrCreateAsync(
                $"issue:{run.Repository}:{run.IssueNumber}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = PipelineIssuesViewBuilder.IssueCacheTtl;
                    return await issueClient.GetIssueAsync(run.IssueNumber, HttpContext.RequestAborted);
                });
            labels = issue?.Labels ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch GitHub labels for issue #{IssueNumber}.", run.IssueNumber);
        }

        return View(new PipelineRunDetailsViewModel(run, logs, labels, issue, dispatches, approvals, evidence) { MaxStageRetries = _options.MaxStageRetries });
    }

    /// <summary>
    /// Returns one of the Cyberpilot pipeline guides in a themed view.
    /// </summary>
    /// <param name="mode">The pipeline mode key: local, cloud, or sdk.</param>
    /// <returns>The requested guide view, or NotFound when the mode is unknown.</returns>
    [HttpGet("Guide/{mode:alpha}")]
    public IActionResult Guide(string mode)
    {
        if (!PipelineGuideHelper.TryRenderGuide(mode, _environment.ContentRootPath, out var viewModel))
        {
            return NotFound();
        }

        return View(viewModel);
    }
}
