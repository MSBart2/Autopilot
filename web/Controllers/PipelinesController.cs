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
    /// Displays open GitHub issues that can launch Cyberpilot.
    /// </summary>
    /// <returns>The issue launcher view.</returns>
    [HttpGet("Issues")]
    public async Task<IActionResult> Issues()
    {
        try
        {
            if (_configHelper.TryGetDefaultConfiguredRepository(out var configuredRepository))
            {
                return await LoadIssuesViewAsync(configuredRepository.Repository, configuredRepository.Repository, configuredRepository.RepoRoot, configuredRepository.Token);
            }

            var issues = await _cache.GetOrCreateAsync(
                $"issues:list:{_options.Repository}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = PipelineIssuesViewBuilder.IssueCacheTtl;
                    return await _issueClient.ListOpenIssuesAsync(HttpContext.RequestAborted);
                }) ?? [];
            return View(await _viewBuilder.BuildIssuesViewModelAsync(issues, _options.Repository, _options.Repository, null, null, HttpContext.RequestAborted));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GitHub issues for Cyberpilot dashboard.");
            return View(await _viewBuilder.BuildIssuesViewModelAsync([], _options.Repository, _options.Repository, null, ex.Message, HttpContext.RequestAborted));
        }
    }

    /// <summary>
    /// Loads open issues from a repository supplied at runtime.
    /// </summary>
    /// <param name="request">The repository connection request.</param>
    /// <returns>The issue launcher view for the requested repository.</returns>
    [HttpPost("Issues/Load")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadIssues(PipelineIssueLoadRequest request)
    {
        if (!ModelState.IsValid || !GitHubRepositoryParser.TryNormalize(request.RepositoryUrl, out var repository))
        {
            return View(nameof(Issues), await _viewBuilder.BuildIssuesViewModelAsync([], _options.Repository, request.RepositoryUrl, null, "Enter a GitHub repository as owner/name or a github.com URL, plus a token.", HttpContext.RequestAborted));
        }

        try
        {
            return await LoadIssuesViewAsync(repository, request.RepositoryUrl, _configHelper.ResolveRepoRoot(_options.RepoRoot), request.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GitHub issues for repository {Repository}.", repository);
            return View(nameof(Issues), await _viewBuilder.BuildIssuesViewModelAsync([], repository, request.RepositoryUrl, null, ex.Message, HttpContext.RequestAborted));
        }
    }

    /// <summary>
    /// Loads open issues from a repository configured in appsettings.
    /// </summary>
    /// <param name="request">The configured repository request.</param>
    /// <returns>The issue launcher view for the configured repository.</returns>
    [HttpPost("Issues/LoadConfigured")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadConfiguredIssues(PipelineConfiguredIssueLoadRequest request)
    {
        if (!ModelState.IsValid
            || !GitHubRepositoryParser.TryNormalize(request.Repository, out var repository)
            || !_configHelper.TryGetConfiguredRepository(repository, out var configuredRepository))
        {
            return View(nameof(Issues), await _viewBuilder.BuildIssuesViewModelAsync([], _options.Repository, request.Repository, null, "Select a configured repository that has a token.", HttpContext.RequestAborted));
        }

        try
        {
            return await LoadIssuesViewAsync(configuredRepository.Repository, configuredRepository.Repository, configuredRepository.RepoRoot, configuredRepository.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GitHub issues for configured repository {Repository}.", configuredRepository.Repository);
            return View(nameof(Issues), await _viewBuilder.BuildIssuesViewModelAsync([], configuredRepository.Repository, configuredRepository.Repository, null, ex.Message, HttpContext.RequestAborted));
        }
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
    /// Starts Cyberpilot for an issue selected from the issue list.
    /// </summary>
    /// <param name="issueNumber">The issue number.</param>
    /// <param name="request">The start request.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("Issues/{issueNumber:int}/Start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartIssue(int issueNumber, PipelineStartRequest request)
    {
        request.IssueNumber = issueNumber;
        return await Start(request);
    }

    /// <summary>
    /// Starts Cyberpilot from a form post.
    /// </summary>
    /// <param name="request">The start request.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("Start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(PipelineStartRequest request)
    {
        if (!ModelState.IsValid)
        {
            TempData["PipelineError"] = "Cyberpilot start request was invalid.";
            return RedirectToAction(nameof(Issues));
        }

        if (!GitHubRepositoryParser.TryNormalize(request.Repository, out var repository))
        {
            TempData["PipelineError"] = "Cyberpilot start request had an invalid repository.";
            return RedirectToAction(nameof(Issues));
        }

        var startError = await ValidateStartRequestAsync(request, repository);
        if (startError is not null)
        {
            TempData["PipelineError"] = startError.Message;
            return RedirectToAction(startError.Action, startError.ActionArgs);
        }

        var customDefinition = await _pipelineAdminStore.FindDefinitionAsync(request.PipelineDefinitionName, HttpContext.RequestAborted);
        BuiltInPipelineCatalog.TryGetDefinition(request.PipelineDefinitionName, out var definition);
        BuiltInPipelineCatalog.TryGetPolicyProfile(request.PolicyProfileName, out var policyProfile);

        var connection = _connectionStore.Get(request.ConnectionId);
        var repoRoot = connection?.RepoRoot ?? _configHelper.ResolveRepoRoot(_options.RepoRoot);

        string? issueTitle = null;
        try
        {
            var issueClient = _configHelper.TryGetConfiguredRepository(repository, out var configuredRepo)
                ? _issueClientFactory.Create(configuredRepo.Repository, configuredRepo.Token)
                : _issueClient;
            var issue = await issueClient.GetIssueAsync(request.IssueNumber, HttpContext.RequestAborted);
            issueTitle = issue?.Title;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch issue title for #{IssueNumber}.", request.IssueNumber);
        }

        var run = new PipelineRun
        {
            IssueNumber = request.IssueNumber,
            Repository = repository,
            Model = request.Model,
            Status = "Queued",
            TriggeredBy = User.Identity?.Name,
            SkipDeliver = request.SkipDeliver,
            StageTimeoutMinutes = request.StageTimeoutMinutes,
            AllowMissingDocs = request.AllowMissingDocs,
            IssueTitle = issueTitle,
            PipelineDefinitionName = definition?.Name ?? customDefinition!.Name,
            PipelineDefinitionVersion = definition?.Version ?? customDefinition!.Version,
            PolicyProfileName = customDefinition?.PolicyProfile.Name ?? policyProfile!.Name,
            ContractVersion = PipelineDefinitionDefaults.ContractVersion,
        };

        _dbContext.PipelineRuns.Add(run);
        await _dbContext.SaveChangesAsync();

        await EnqueueRunAsync(run, repoRoot, connection?.Token);

        return RedirectToAction(nameof(Details), new { id = run.Id });
    }

    private sealed record StartValidationError(string Message, string Action, object? ActionArgs = null);

    private async Task<StartValidationError?> ValidateStartRequestAsync(PipelineStartRequest request, string repository)
    {
        var customDefinition = await _pipelineAdminStore.FindDefinitionAsync(request.PipelineDefinitionName, HttpContext.RequestAborted);
        if (!BuiltInPipelineCatalog.TryGetDefinition(request.PipelineDefinitionName, out _) && customDefinition is null)
        {
            return new StartValidationError(
                $"Unsupported pipeline definition '{request.PipelineDefinitionName}'. Available definitions: {BuiltInPipelineCatalog.AvailableDefinitionNames}.",
                nameof(Issues));
        }

        if (customDefinition is null && !BuiltInPipelineCatalog.TryGetPolicyProfile(request.PolicyProfileName, out _))
        {
            return new StartValidationError(
                $"Unsupported policy profile '{request.PolicyProfileName}'. Available profiles: {BuiltInPipelineCatalog.AvailablePolicyProfileNames}.",
                nameof(Issues));
        }

        var connection = _connectionStore.Get(request.ConnectionId);
        if (!string.IsNullOrWhiteSpace(request.ConnectionId)
            && (connection is null || !connection.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)))
        {
            return new StartValidationError("The repository token expired. Load issues again before starting Cyberpilot.", nameof(Issues));
        }

        var hasActiveRun = await _dbContext.PipelineRuns.AnyAsync(run =>
            run.Repository == repository && run.IssueNumber == request.IssueNumber && (run.Status == "Queued" || run.Status == "Running" || run.Status == "Pausing"));
        if (hasActiveRun)
        {
            return new StartValidationError(
                $"{repository} issue #{request.IssueNumber} already has an active Cyberpilot run.",
                nameof(Issues));
        }

        return null;
    }

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
        if (run is null)
        {
            return NotFound();
        }

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
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status == "Running")
        {
            run.Status = "Pausing";
            await _dbContext.SaveChangesAsync();
            TempData["PipelineNotice"] = "Pause requested ΓÇö the pipeline will pause after the current stage completes.";
        }
        else
        {
            TempData["PipelineError"] = "Only running pipelines can be paused.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

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
        if (run is null)
        {
            return NotFound();
        }

        var approval = await _dbContext.PipelineApprovals.FirstOrDefaultAsync(item => item.Id == approvalId && item.RunId == id);
        if (approval is null)
        {
            return NotFound();
        }

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
        var issues = await _cache.GetOrCreateAsync(
            $"issues:list:{repository}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PipelineIssuesViewBuilder.IssueCacheTtl;
                return await client.ListOpenIssuesAsync(HttpContext.RequestAborted);
            }) ?? [];
        var connectionId = _connectionStore.Save(repository, repoRoot, token);
        return View(nameof(Issues), await _viewBuilder.BuildIssuesViewModelAsync(issues, repository, repositoryInput, connectionId, null, HttpContext.RequestAborted));
    }

    private ValueTask EnqueueRunAsync(PipelineRun run, string repoRoot, string? token, string? retryReason = null)
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
            string.IsNullOrWhiteSpace(retryReason) ? null : retryReason));
    }
}
