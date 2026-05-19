using System.Diagnostics;
using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Cyberpilot.Web.Controllers;

/// <summary>
/// Displays the Cyberpilot pipeline modes, stages, and implementation assets.
/// </summary>
[Route("[controller]")]
public partial class PipelinesController : Controller
{
    private readonly CyberpilotDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly ICyberpilotRunQueue _queue;
    private readonly IGitHubIssueClient _issueClient;
    private readonly IGitHubIssueClientFactory _issueClientFactory;
    private readonly IRepositoryConnectionStore _connectionStore;
    private readonly IMemoryCache _cache;
    private readonly CyberpilotWebOptions _options;
    private readonly ILogger<PipelinesController> _logger;
    private readonly RepositoryConfigurationHelper _configHelper;
    private readonly PipelineIssuesViewBuilder _viewBuilder;
    private readonly IPipelineDefinitionAdminStore _pipelineAdminStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelinesController"/> class.
    /// </summary>
    /// <param name="dbContext">Pipeline persistence context.</param>
    /// <param name="environment">Host environment details.</param>
    /// <param name="queue">Background run queue.</param>
    /// <param name="issueClient">GitHub issue client.</param>
    /// <param name="issueClientFactory">Factory for runtime repository issue clients.</param>
    /// <param name="connectionStore">Short-lived repository credential store.</param>
    /// <param name="cache">In-memory cache for GitHub API responses.</param>
    /// <param name="options">Pipeline web options.</param>
    /// <param name="pipelineAdminStore">Editable pipeline definition store.</param>
    /// <param name="logger">Controller logger.</param>
    public PipelinesController(
        CyberpilotDbContext dbContext,
        IWebHostEnvironment environment,
        ICyberpilotRunQueue queue,
        IGitHubIssueClient issueClient,
        IGitHubIssueClientFactory issueClientFactory,
        IRepositoryConnectionStore connectionStore,
        IMemoryCache cache,
        IOptions<CyberpilotWebOptions> options,
        IPipelineDefinitionAdminStore pipelineAdminStore,
        ILogger<PipelinesController> logger)
    {
        _dbContext = dbContext;
        _environment = environment;
        _queue = queue;
        _issueClient = issueClient;
        _issueClientFactory = issueClientFactory;
        _connectionStore = connectionStore;
        _cache = cache;
        _options = options.Value;
        _pipelineAdminStore = pipelineAdminStore;
        _logger = logger;
        _configHelper = new RepositoryConfigurationHelper(_options, environment, logger);
        _viewBuilder = new PipelineIssuesViewBuilder(_dbContext, cache, _configHelper, _options, pipelineAdminStore);
    }

    // ── Shared private helpers ────────────────────────────────────────────────

    private IActionResult RedirectWithError(string action, object routeValues, string error)
    {
        TempData["PipelineError"] = error;
        return RedirectToAction(action, routeValues);
    }

    private (string RepoRoot, string? Token) ResolveRepoConfig(string repository)
    {
        var repoRoot = _configHelper.ResolveRepoRoot(_options.RepoRoot);
        string? token = null;
        if (_configHelper.TryGetConfiguredRepository(repository, out var configured))
        {
            repoRoot = configured.RepoRoot;
            token = configured.Token;
        }
        return (repoRoot, token);
    }

    private async Task<IActionResult?> CheckConflictingRunAsync(PipelineRun run, string id)
    {
        var hasActiveRun = await _dbContext.PipelineRuns.AnyAsync(item =>
            item.Id != run.Id
            && item.Repository == run.Repository
            && item.IssueNumber == run.IssueNumber
            && (item.Status == "Queued" || item.Status == "Running" || item.Status == "Pausing"));
        if (!hasActiveRun) return null;
        return RedirectWithError(nameof(Details), new { id }, $"{run.Repository} issue #{run.IssueNumber} already has an active Cyberpilot run.");
    }

    private async Task<IActionResult?> CheckApprovalBlockersAsync(string runId, string id)
    {
        var hasPendingApproval = await _dbContext.PipelineApprovals.AnyAsync(approval =>
            approval.RunId == runId && approval.Status == nameof(ApprovalStatus.Pending));
        if (hasPendingApproval)
            return RedirectWithError(nameof(Details), new { id }, "Resolve pending approvals before continuing this run.");

        var hasRejectedApproval = await _dbContext.PipelineApprovals.AnyAsync(approval =>
            approval.RunId == runId && approval.Status == nameof(ApprovalStatus.Rejected));
        if (hasRejectedApproval)
            return RedirectWithError(nameof(Details), new { id }, "Rejected approvals must be addressed with a targeted retry or rework before this run can continue.");

        return null;
    }

    private async Task<IActionResult> LoadIssuesViewAsync(string repository, string repositoryInput, string repoRoot, string token)
    {
        var client = _issueClientFactory.Create(repository, token);
        var issuesTask = _cache.GetOrCreateAsync(
            $"issues:list:{repository}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PipelineIssuesViewBuilder.IssueCacheTtl;
                return await client.ListOpenIssuesAsync(HttpContext.RequestAborted);
            });
        var pullRequestsTask = _cache.GetOrCreateAsync(
            $"prs:list:{repository}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PipelineIssuesViewBuilder.IssueCacheTtl;
                return await client.ListOpenPullRequestsAsync(HttpContext.RequestAborted);
            });
        await Task.WhenAll(issuesTask, pullRequestsTask);
        var issues = await issuesTask ?? [];
        var pullRequests = await pullRequestsTask ?? [];
        var connectionId = _connectionStore.Save(repository, repoRoot, token);
        return View(nameof(Issues), await _viewBuilder.BuildIssuesViewModelAsync(issues, pullRequests, repository, repositoryInput, connectionId, null, HttpContext.RequestAborted));
    }

    private ValueTask EnqueueRunAsync(
        PipelineRun run,
        string repoRoot,
        string? token,
        string? retryReason = null,
        string? prHeadBranch = null,
        int? prNumber = null,
        IReadOnlyDictionary<string, string>? stageModelOverrides = null,
        IReadOnlyDictionary<string, string>? stageModelFallbacks = null)
    {
        return _queue.EnqueueAsync(new WebPipelineRunRequest(
            run.Id,
            run.IssueNumber,
            run.Repository,
            repoRoot,
            _configHelper.ResolveAgentPromptRoot(),
            string.IsNullOrWhiteSpace(token) ? null : token,
            run.Model,
            run.SkipDeliver,
            TimeSpan.FromMinutes(run.StageTimeoutMinutes),
            run.AllowMissingDocs,
            run.CurrentStage,
            run.PipelineDefinitionName,
            run.PipelineDefinitionVersion,
            run.PolicyProfileName,
            run.ContractVersion,
            System.IO.File.Exists(_pipelineAdminStore.DefinitionFilePath) ? _pipelineAdminStore.DefinitionFilePath : null,
            string.IsNullOrWhiteSpace(retryReason) ? null : retryReason,
            string.IsNullOrWhiteSpace(prHeadBranch) ? null : prHeadBranch,
            prNumber,
            stageModelOverrides,
            stageModelFallbacks));
    }
}
