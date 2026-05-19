using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Markdig;
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

        var artifacts = await _dbContext.PipelineArtifacts
            .Where(item => item.RunId == id)
            .OrderBy(item => item.StageName)
            .ThenBy(item => item.Name)
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

        return View(new PipelineRunDetailsViewModel(run, logs, labels, issue, dispatches, approvals, evidence, artifacts) { MaxStageRetries = _options.MaxStageRetries });
    }

    /// <summary>
    /// Displays the latest captured plan for a pipeline run as a standalone document.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>The plan document view, or NotFound when the run or plan output does not exist.</returns>
    [HttpGet("{id}/Plan")]
    public async Task<IActionResult> Plan(string id)
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

        var evidence = await _dbContext.PipelineEvidence
            .Where(item => item.RunId == id)
            .OrderBy(item => item.StageName)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.CreatedAt)
            .AsNoTracking()
            .ToArrayAsync();

        var plan = PipelinePlanReviewViewModel.Create(run, logs, evidence);
        if (plan is null)
        {
            return NotFound();
        }

        string? renderedHtml = null;
        if (!string.IsNullOrWhiteSpace(plan.FullPlanText))
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
            renderedHtml = Markdown.ToHtml(plan.FullPlanText, pipeline);
        }

        return View(new PipelinePlanDocumentViewModel(run, plan, renderedHtml));
    }

    /// <summary>
    /// Displays the latest captured triage report for a pipeline run as a standalone document.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>The triage document view, or NotFound when the run or triage output does not exist.</returns>
    [HttpGet("{id}/Triage")]
    public async Task<IActionResult> Triage(string id)
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

        var evidence = await _dbContext.PipelineEvidence
            .Where(item => item.RunId == id)
            .OrderBy(item => item.StageName)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.CreatedAt)
            .AsNoTracking()
            .ToArrayAsync();

        var triage = PipelineTriageReviewViewModel.Create(run, logs, evidence);
        if (triage is null)
        {
            return NotFound();
        }

        string? renderedHtml = null;
        if (!string.IsNullOrWhiteSpace(triage.FullTriageText))
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
            renderedHtml = Markdown.ToHtml(triage.FullTriageText, pipeline);
        }

        return View(new PipelineTriageDocumentViewModel(run, triage, renderedHtml));
    }

    /// <summary>
    /// Displays the latest captured implementation output for a pipeline run as a standalone document.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>The implementation document view, or NotFound when the run or stage output does not exist.</returns>
    [HttpGet("{id}/Implement")]
    public Task<IActionResult> Implement(string id) => StageDocument(id, "implement");

    /// <summary>
    /// Displays the latest captured review output for a pipeline run as a standalone document.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>The review document view, or NotFound when the run or stage output does not exist.</returns>
    [HttpGet("{id}/Review")]
    public Task<IActionResult> Review(string id) => StageDocument(id, "review");

    /// <summary>
    /// Displays the latest captured documentation output for a pipeline run as a standalone document.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>The docs document view, or NotFound when the run or stage output does not exist.</returns>
    [HttpGet("{id}/Docs")]
    public Task<IActionResult> Docs(string id) => StageDocument(id, "docs");

    /// <summary>
    /// Displays the latest captured delivery output for a pipeline run as a standalone document.
    /// </summary>
    /// <param name="id">The run identifier.</param>
    /// <returns>The delivery document view, or NotFound when the run or stage output does not exist.</returns>
    [HttpGet("{id}/Deliver")]
    public Task<IActionResult> Deliver(string id) => StageDocument(id, "deliver");

    private async Task<IActionResult> StageDocument(string id, string stageName)
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

        var evidence = await _dbContext.PipelineEvidence
            .Where(item => item.RunId == id)
            .OrderBy(item => item.StageName)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.CreatedAt)
            .AsNoTracking()
            .ToArrayAsync();

        var stage = PipelineStageOutputViewModel.Create(stageName, run, logs, evidence);
        if (stage is null)
        {
            return NotFound();
        }

        string? renderedHtml = null;
        if (!string.IsNullOrWhiteSpace(stage.FullOutputText))
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
            renderedHtml = Markdown.ToHtml(stage.FullOutputText, pipeline);
        }

        return View("Stage", new PipelineStageDocumentViewModel(run, stage, renderedHtml));
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
