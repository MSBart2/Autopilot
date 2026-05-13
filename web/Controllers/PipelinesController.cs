using System.Diagnostics;
using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Cyberpilot.Web.Controllers;

/// <summary>
/// Displays the Cyberpilot pipeline modes, stages, and implementation assets.
/// </summary>
[Route("[controller]")]
public class PipelinesController : Controller
{
    private static readonly string[] AgentCommentMarkers =
    [
        "## 🕵️ Case File",
        "## 🎯 The Playbook",
        "## 🚀 Mission Control — Landing Report",
        "SDK Cyberpilot branch ready:",
        "Planning Started",
        "Research Complete",
        "Branch Ready",
        "build-complete",
        "human verification",
    ];

    private static readonly IReadOnlyDictionary<string, GuideDefinition> GuideFiles = new Dictionary<string, GuideDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["local"] = new("AI-SDLC.md", "Local", "Controller Session", "VS Code Copilot Chat or Copilot CLI orchestration with repository agents.", "Local Mode"),
        ["cloud"] = new("AI-SDLC.md", "Cloud", "Actions Orbit", "GitHub Agentic Workflow automation with review and finish gates.", "Cloud Mode"),
        ["sdk"] = new("AI-SDLC.md", "SDK", "Web Dispatch", "Programmatic Copilot SDK execution for repeatable issue-to-PR workflows.", "SDK Mode")
    };

    private static readonly MarkdownPipeline GuideMarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private readonly CyberpilotDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly ICyberpilotRunQueue _queue;
    private readonly IGitHubIssueClient _issueClient;
    private readonly IGitHubIssueClientFactory _issueClientFactory;
    private readonly IRepositoryConnectionStore _connectionStore;
    private readonly IMemoryCache _cache;
    private readonly CyberpilotWebOptions _options;
    private readonly ILogger<PipelinesController> _logger;

    private static readonly TimeSpan IssueCacheTtl = TimeSpan.FromMinutes(30);

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
        _logger = logger;
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

        return View(BuildDashboard(runs));
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
            if (TryGetDefaultConfiguredRepository(out var configuredRepository))
            {
                return await LoadIssuesViewAsync(configuredRepository.Repository, configuredRepository.Repository, configuredRepository.RepoRoot, configuredRepository.Token);
            }

            var issues = await _cache.GetOrCreateAsync(
                $"issues:list:{_options.Repository}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = IssueCacheTtl;
                    return await _issueClient.ListOpenIssuesAsync(HttpContext.RequestAborted);
                }) ?? [];
            return View(await BuildIssuesViewModelAsync(issues, _options.Repository, _options.Repository, null, null));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GitHub issues for Cyberpilot dashboard.");
            return View(await BuildIssuesViewModelAsync([], _options.Repository, _options.Repository, null, ex.Message));
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
            return View(nameof(Issues), await BuildIssuesViewModelAsync([], _options.Repository, request.RepositoryUrl, null, "Enter a GitHub repository as owner/name or a github.com URL, plus a token."));
        }

        try
        {
            return await LoadIssuesViewAsync(repository, request.RepositoryUrl, ResolveRepoRoot(_options.RepoRoot), request.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GitHub issues for repository {Repository}.", repository);
            return View(nameof(Issues), await BuildIssuesViewModelAsync([], repository, request.RepositoryUrl, null, ex.Message));
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
            || !TryGetConfiguredRepository(repository, out var configuredRepository))
        {
            return View(nameof(Issues), await BuildIssuesViewModelAsync([], _options.Repository, request.Repository, null, "Select a configured repository that has a token."));
        }

        try
        {
            return await LoadIssuesViewAsync(configuredRepository.Repository, configuredRepository.Repository, configuredRepository.RepoRoot, configuredRepository.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GitHub issues for configured repository {Repository}.", configuredRepository.Repository);
            return View(nameof(Issues), await BuildIssuesViewModelAsync([], configuredRepository.Repository, configuredRepository.Repository, null, ex.Message));
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

        GitHubIssueSummary? issue = null;
        IReadOnlyList<string> labels = [];
        try
        {
            var issueClient = TryGetConfiguredRepository(run.Repository, out var configuredRepository)
                ? _issueClientFactory.Create(configuredRepository.Repository, configuredRepository.Token)
                : _issueClient;

            issue = await _cache.GetOrCreateAsync(
                $"issue:{run.Repository}:{run.IssueNumber}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = IssueCacheTtl;
                    return await issueClient.GetIssueAsync(run.IssueNumber, HttpContext.RequestAborted);
                });
            labels = issue?.Labels ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch GitHub labels for issue #{IssueNumber}.", run.IssueNumber);
        }

        return View(new PipelineRunDetailsViewModel(run, logs, labels, issue, dispatches) { MaxStageRetries = _options.MaxStageRetries });
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

        var connection = _connectionStore.Get(request.ConnectionId);
        if (!string.IsNullOrWhiteSpace(request.ConnectionId)
            && (connection is null || !connection.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)))
        {
            TempData["PipelineError"] = "The repository token expired. Load issues again before starting Cyberpilot.";
            return RedirectToAction(nameof(Issues));
        }

        var repoRoot = connection?.RepoRoot ?? ResolveRepoRoot(_options.RepoRoot);

        var hasActiveRun = await _dbContext.PipelineRuns.AnyAsync(run =>
            run.Repository == repository && run.IssueNumber == request.IssueNumber && (run.Status == "Queued" || run.Status == "Running" || run.Status == "Pausing"));
        if (hasActiveRun)
        {
            TempData["PipelineError"] = $"{repository} issue #{request.IssueNumber} already has an active Cyberpilot run.";
            return RedirectToAction(nameof(Issues));
        }

        // Best-effort fetch of the issue title for dashboard display.
        string? issueTitle = null;
        try
        {
            var issueClient = TryGetConfiguredRepository(repository, out var configuredRepo)
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
            PipelineDefinitionName = PipelineDefinitionDefaults.DefinitionName,
            PipelineDefinitionVersion = PipelineDefinitionDefaults.DefinitionVersion,
            PolicyProfileName = PipelineDefinitionDefaults.PolicyProfileName,
            ContractVersion = PipelineDefinitionDefaults.ContractVersion,
        };

        _dbContext.PipelineRuns.Add(run);
        await _dbContext.SaveChangesAsync();

        await EnqueueRunAsync(run, repoRoot, connection?.Token);

        return RedirectToAction(nameof(Details), new { id = run.Id });
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
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status is "Queued" or "Running" or "Pausing")
        {
            TempData["PipelineError"] = "This run is already active.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (run.Status is not ("Failed" or "Stopped" or "Paused" or "Cancelled"))
        {
            TempData["PipelineError"] = "This run cannot be continued from its current status.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var hasActiveRun = await _dbContext.PipelineRuns.AnyAsync(item =>
            item.Id != run.Id
            && item.Repository == run.Repository
            && item.IssueNumber == run.IssueNumber
            && (item.Status == "Queued" || item.Status == "Running" || item.Status == "Pausing"));
        if (hasActiveRun)
        {
            TempData["PipelineError"] = $"{run.Repository} issue #{run.IssueNumber} already has an active Cyberpilot run.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var repoRoot = ResolveRepoRoot(_options.RepoRoot);
        string? token = null;
        if (TryGetConfiguredRepository(run.Repository, out var configuredRepository))
        {
            repoRoot = configuredRepository.RepoRoot;
            token = configuredRepository.Token;
        }

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
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status is "Queued" or "Running" or "Pausing")
        {
            TempData["PipelineError"] = "This run is already active.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!await IsReviewReworkCandidateAsync(run))
        {
            TempData["PipelineError"] = "Rework from Review is only available for stopped or failed review runs.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var hasActiveRun = await _dbContext.PipelineRuns.AnyAsync(item =>
            item.Id != run.Id
            && item.Repository == run.Repository
            && item.IssueNumber == run.IssueNumber
            && (item.Status == "Queued" || item.Status == "Running" || item.Status == "Pausing"));
        if (hasActiveRun)
        {
            TempData["PipelineError"] = $"{run.Repository} issue #{run.IssueNumber} already has an active Cyberpilot run.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var repoRoot = ResolveRepoRoot(_options.RepoRoot);
        string? token = null;
        if (TryGetConfiguredRepository(run.Repository, out var configuredRepository))
        {
            repoRoot = configuredRepository.RepoRoot;
            token = configuredRepository.Token;
        }

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
        if (run is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        if (run.Status is "Queued" or "Running" or "Pausing")
        {
            TempData["PipelineError"] = "This run is already active.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (run.Status is not ("Failed" or "Stopped" or "Paused" or "Cancelled"))
        {
            TempData["PipelineError"] = "This run cannot be retried from its current status.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var hasActiveRun = await _dbContext.PipelineRuns.AnyAsync(item =>
            item.Id != run.Id
            && item.Repository == run.Repository
            && item.IssueNumber == run.IssueNumber
            && (item.Status == "Queued" || item.Status == "Running" || item.Status == "Pausing"));
        if (hasActiveRun)
        {
            TempData["PipelineError"] = $"{run.Repository} issue #{run.IssueNumber} already has an active Cyberpilot run.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!PipelineRunDetailsViewModel.ValidStageNames.Any(s => s.Equals(request.StageName, StringComparison.OrdinalIgnoreCase)))
        {
            TempData["PipelineError"] = $"'{request.StageName}' is not a recognized pipeline stage.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ToLower() is used here because EF Core cannot translate StringComparison enum overloads to SQL.
        var stageLogCount = await _dbContext.PipelineStageLogs.CountAsync(log =>
            log.RunId == id && log.StageName.ToLower() == request.StageName.ToLower());
        if (stageLogCount >= _options.MaxStageRetries)
        {
            TempData["PipelineError"] = $"Maximum retry attempts reached for the '{request.StageName}' stage.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            run.Model = request.Model;
        }

        if (request.StageTimeoutMinutes.HasValue)
        {
            run.StageTimeoutMinutes = request.StageTimeoutMinutes.Value;
        }

        var repoRoot = ResolveRepoRoot(_options.RepoRoot);
        string? token = null;
        if (TryGetConfiguredRepository(run.Repository, out var configuredRepository))
        {
            repoRoot = configuredRepository.RepoRoot;
            token = configuredRepository.Token;
        }

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
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status != "Completed" || !run.SkipDeliver)
        {
            TempData["PipelineError"] = "Deliver is only available for completed runs that skipped delivery.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var repoRoot = ResolveRepoRoot(_options.RepoRoot);
        string? token = null;
        if (TryGetConfiguredRepository(run.Repository, out var configuredRepository))
        {
            repoRoot = configuredRepository.RepoRoot;
            token = configuredRepository.Token;
        }

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
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status is "Queued" or "Running" or "Pausing")
        {
            TempData["PipelineError"] = "Cancel or finish the active run before resetting the mission.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (IsDeliveredRun(run))
        {
            TempData["PipelineError"] = "Reset Mission is not available after the code has been delivered.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var repoRoot = ResolveRepoRoot(_options.RepoRoot);
        string? token = null;
        if (TryGetConfiguredRepository(run.Repository, out var configuredRepository))
        {
            repoRoot = configuredRepository.RepoRoot;
            token = configuredRepository.Token;
        }

        var issueClient = string.IsNullOrWhiteSpace(token)
            ? _issueClient
            : _issueClientFactory.Create(run.Repository, token);
        try
        {
            var issue = await issueClient.GetIssueAsync(run.IssueNumber, HttpContext.RequestAborted);
            await ResetIssueLabelsAsync(issueClient, run.IssueNumber, HttpContext.RequestAborted);
            var deletedComments = await DeleteAgentCommentsAsync(issueClient, run.IssueNumber, HttpContext.RequestAborted);
            var branchName = run.BranchName;
            if (string.IsNullOrWhiteSpace(branchName))
            {
                branchName = BranchProvisioner.CreateBranchName(run.IssueNumber, issue?.Title ?? $"issue-{run.IssueNumber}");
            }

            var branchDeleted = await DeleteIssueBranchAsync(repoRoot, branchName, HttpContext.RequestAborted);

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
            TempData["PipelineNotice"] = "Pause requested — the pipeline will pause after the current stage completes.";
        }
        else
        {
            TempData["PipelineError"] = "Only running pipelines can be paused.";
        }

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
        if (!GuideFiles.TryGetValue(mode, out var guide))
        {
            return NotFound();
        }

        var repositoryRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, ".."));
        var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, guide.FileName));
        if (!fullPath.StartsWith(repositoryRoot, StringComparison.Ordinal) || !System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var markdown = System.IO.File.ReadAllText(fullPath);
        var modeMarkdown = ExtractModeContent(markdown, guide.SectionHeading);
        var html = Markdown.ToHtml(modeMarkdown, GuideMarkdownPipeline);
        return View(new PipelineGuideViewModel(
            Mode: guide.Mode,
            Title: guide.Title,
            Summary: guide.Summary,
            HtmlContent: html,
            SourceFileName: guide.FileName));
    }

    private static string ExtractModeContent(string markdown, string sectionHeading)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        var introLines = new List<string>();
        var firstSectionIndex = Array.FindIndex(lines, line => line.StartsWith("## ", StringComparison.Ordinal));
        var introEnd = firstSectionIndex >= 0 ? firstSectionIndex : lines.Length;
        for (var index = 0; index < introEnd; index++)
        {
            introLines.Add(lines[index]);
        }

        var modeLines = ExtractSection(lines, sectionHeading);
        if (modeLines.Count == 0)
        {
            modeLines = lines.ToList();
        }

        var combined = string.Join('\n', introLines).TrimEnd();
        if (modeLines.Count == 0)
        {
            return combined;
        }

        return string.Concat(combined, "\n\n---\n\n", string.Join('\n', modeLines).Trim());
    }

    private static List<string> ExtractSection(string[] lines, string sectionHeading)
    {
        var sectionTitle = $"## {sectionHeading}";
        var sectionLines = new List<string>();
        var inSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (!inSection)
            {
                if (line.Equals(sectionTitle, StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    sectionLines.Add(rawLine);
                }

                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            sectionLines.Add(rawLine);
        }

        return sectionLines;
    }

    private static PipelineDashboardViewModel BuildDashboard(IReadOnlyList<PipelineRun> runs)
    {
        return new PipelineDashboardViewModel { Runs = runs };
    }

    private async Task<PipelineIssuesViewModel> BuildIssuesViewModelAsync(
        IReadOnlyList<GitHubIssueSummary> issues,
        string repository,
        string repositoryInput,
        string? connectionId,
        string? error)
    {
        var recentRuns = await _dbContext.PipelineRuns
            .AsNoTracking()
            .Where(run => run.Repository == repository)
            .OrderByDescending(run => run.CreatedAt)
            .ToArrayAsync(HttpContext.RequestAborted);

        var latestRunsByIssue = recentRuns
            .GroupBy(run => run.IssueNumber)
            .Select(group => group.First())
            .ToArray();

        var sdkActiveIssueNumbers = latestRunsByIssue
            .Where(run => run.Status is "Queued" or "Running" or "Pausing")
            .Select(run => run.IssueNumber)
            .ToHashSet();

        var latestSdkRunIds = latestRunsByIssue.ToDictionary(run => run.IssueNumber, run => run.Id);

        return new PipelineIssuesViewModel(issues, repository, repositoryInput, connectionId, error, GetConfiguredRepositoryChoices(), sdkActiveIssueNumbers, latestSdkRunIds);
    }

    private async Task<IActionResult> LoadIssuesViewAsync(string repository, string repositoryInput, string repoRoot, string token)
    {
        var client = _issueClientFactory.Create(repository, token);
        var issues = await _cache.GetOrCreateAsync(
            $"issues:list:{repository}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = IssueCacheTtl;
                return await client.ListOpenIssuesAsync(HttpContext.RequestAborted);
            }) ?? [];
        var connectionId = _connectionStore.Save(repository, repoRoot, token);
        return View(nameof(Issues), await BuildIssuesViewModelAsync(issues, repository, repositoryInput, connectionId, null));
    }

    private static async Task ResetIssueLabelsAsync(IGitHubIssueClient issueClient, int issueNumber, CancellationToken cancellationToken)
    {
        var labels = await issueClient.GetIssueLabelsAsync(issueNumber, cancellationToken);
        foreach (var label in labels.Where(label => label.StartsWith("sdk/", StringComparison.OrdinalIgnoreCase)))
        {
            await issueClient.RemoveIssueLabelAsync(issueNumber, label, cancellationToken);
        }

        if (!labels.Contains("sdk", StringComparer.OrdinalIgnoreCase))
        {
            await issueClient.AddIssueLabelAsync(issueNumber, "sdk", cancellationToken);
        }
    }

    private static async Task<int> DeleteAgentCommentsAsync(IGitHubIssueClient issueClient, int issueNumber, CancellationToken cancellationToken)
    {
        var comments = await issueClient.ListIssueCommentsAsync(issueNumber, cancellationToken);
        var deleted = 0;
        foreach (var comment in comments.Where(comment => IsAgentComment(comment.Body)))
        {
            await issueClient.DeleteIssueCommentAsync(comment.Id, cancellationToken);
            deleted++;
        }

        return deleted;
    }

    private static bool IsAgentComment(string body)
        => AgentCommentMarkers.Any(marker => body.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsDeliveredRun(PipelineRun run)
        => run.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !run.SkipDeliver;

    private static async Task<bool> DeleteIssueBranchAsync(string repoRoot, string branchName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchName) || !Directory.Exists(repoRoot))
        {
            return false;
        }

        var deletedRemote = await GitSucceedsAsync(repoRoot, ["push", "origin", "--delete", branchName], cancellationToken);
        var currentBranch = (await RunGitAsync(repoRoot, ["branch", "--show-current"], true, cancellationToken)).Trim();
        if (currentBranch.Equals(branchName, StringComparison.OrdinalIgnoreCase))
        {
            var defaultBranch = await ResolveDefaultBranchAsync(repoRoot, cancellationToken);
            await GitSucceedsAsync(repoRoot, ["switch", defaultBranch], cancellationToken);
        }

        var deletedLocal = await GitSucceedsAsync(repoRoot, ["branch", "-D", branchName], cancellationToken);
        return deletedRemote || deletedLocal;
    }

    private static async Task<string> ResolveDefaultBranchAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var remoteHead = (await RunGitAsync(repoRoot, ["symbolic-ref", "refs/remotes/origin/HEAD", "--short"], true, cancellationToken)).Trim();
        if (remoteHead.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
        {
            return remoteHead["origin/".Length..];
        }

        return "main";
    }

    private static async Task<bool> GitSucceedsAsync(string repoRoot, IReadOnlyList<string> args, CancellationToken cancellationToken)
        => (await RunGitProcessAsync(repoRoot, args, true, cancellationToken)) == 0;

    private static async Task<string> RunGitAsync(string repoRoot, IReadOnlyList<string> args, bool allowFailure, CancellationToken cancellationToken)
    {
        var (exitCode, output, error) = await RunGitProcessAsync(repoRoot, args, allowFailure, cancellationToken, captureOutput: true);
        if (exitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed with exit code {exitCode}: {error}");
        }

        return output;
    }

    private static async Task<int> RunGitProcessAsync(string repoRoot, IReadOnlyList<string> args, bool allowFailure, CancellationToken cancellationToken)
    {
        var (exitCode, _, error) = await RunGitProcessAsync(repoRoot, args, allowFailure, cancellationToken, captureOutput: false);
        if (exitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed with exit code {exitCode}: {error}");
        }

        return exitCode;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunGitProcessAsync(string repoRoot, IReadOnlyList<string> args, bool allowFailure, CancellationToken cancellationToken, bool captureOutput)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, captureOutput ? output : string.Empty, error);
    }

    private ValueTask EnqueueRunAsync(PipelineRun run, string repoRoot, string? token, string? retryReason = null)
    {
        return _queue.EnqueueAsync(new WebPipelineRunRequest(
            run.Id,
            run.IssueNumber,
            run.Repository,
            repoRoot,
            ResolveAgentPromptRoot(),
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
            string.IsNullOrWhiteSpace(retryReason) ? null : retryReason));
    }

    private async Task<bool> IsReviewReworkCandidateAsync(PipelineRun run)
    {
        if (run.IsRemote || run.Status is not ("Failed" or "Stopped"))
        {
            return false;
        }

        if (run.CurrentStage?.Equals("review", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var latestBlockedStage = await _dbContext.PipelineStageLogs
            .Where(log => log.RunId == run.Id && (log.Status == "STOP" || log.Status == "INVALID" || log.Status == "failed"))
            .OrderByDescending(log => log.CompletedAt ?? log.StartedAt)
            .Select(log => log.StageName)
            .FirstOrDefaultAsync();

        return latestBlockedStage?.Equals("review", StringComparison.OrdinalIgnoreCase) == true;
    }

    private IReadOnlyList<ConfiguredRepositoryViewModel> GetConfiguredRepositoryChoices()
    {
        return _options.Repositories
            .Select(repository => TryBuildConfiguredRepository(repository, out var configured) ? configured : null)
            .Where(repository => repository is not null)
            .Select(repository => new ConfiguredRepositoryViewModel(
                string.IsNullOrWhiteSpace(repository!.Name) ? repository.Repository : repository.Name,
                repository.Repository,
                repository.RepoRoot))
            .DistinctBy(repository => repository.Repository, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool TryGetDefaultConfiguredRepository(out RuntimeConfiguredRepository repository)
    {
        repository = default!;
        var configuredRepositories = _options.Repositories
            .Select(option => TryBuildConfiguredRepository(option, out var configured) ? configured : null)
            .Where(configured => configured is not null && !string.IsNullOrWhiteSpace(configured.Token))
            .Cast<RuntimeConfiguredRepository>()
            .ToArray();

        if (configuredRepositories.Length == 0)
        {
            return false;
        }

        repository = configuredRepositories.FirstOrDefault(configured =>
            configured.Repository.Equals(_options.Repository, StringComparison.OrdinalIgnoreCase))
            ?? configuredRepositories[0];
        return true;
    }

    private bool TryGetConfiguredRepository(string repository, out RuntimeConfiguredRepository configuredRepository)
    {
        configuredRepository = default!;
        _logger.LogDebug("TryGetConfiguredRepository: Looking for {Repository} among {Count} configured repositories", repository, _options.Repositories.Count);
        foreach (var option in _options.Repositories)
        {
            if (TryBuildConfiguredRepository(option, out var configured)
                && configured.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(configured.Token))
            {
                _logger.LogDebug("TryGetConfiguredRepository: Found matching configured repository {ConfiguredRepo}", configured.Repository);
                configuredRepository = configured;
                return true;
            }
        }

        _logger.LogDebug("TryGetConfiguredRepository: No matching configured repository found for {Repository}", repository);
        return false;
    }

    private bool TryBuildConfiguredRepository(ConfiguredRepositoryOptions option, out RuntimeConfiguredRepository configuredRepository)
    {
        configuredRepository = default!;
        if (!GitHubRepositoryParser.TryNormalize(option.Repository, out var repository))
        {
            return false;
        }

        configuredRepository = new RuntimeConfiguredRepository(option.Name, repository, ResolveRepoRoot(option.RepoRoot), option.Token);
        return true;
    }

    private string ResolveRepoRoot(string? repoRoot)
    {
        var value = string.IsNullOrWhiteSpace(repoRoot) ? _options.RepoRoot : repoRoot;
        return Path.GetFullPath(value);
    }

    private string ResolveAgentPromptRoot()
    {
        var value = string.IsNullOrWhiteSpace(_options.AgentPromptRoot)
            ? Path.Combine(_environment.ContentRootPath, "..")
            : _options.AgentPromptRoot;
        return Path.GetFullPath(value);
    }

    private sealed record RuntimeConfiguredRepository(string Name, string Repository, string RepoRoot, string Token);

    private sealed record GuideDefinition(string FileName, string Mode, string Title, string Summary, string SectionHeading);

    private string? ResolveRemoteToken(string repository)
    {
        _logger.LogDebug("ResolveRemoteToken: Checking for configured repository {Repository}", repository);
        if (TryGetConfiguredRepository(repository, out var configured) && !string.IsNullOrWhiteSpace(configured.Token))
        {
            _logger.LogInformation("ResolveRemoteToken: Found configured repository {Repository}", repository);
            return configured.Token;
        }

        _logger.LogDebug("ResolveRemoteToken: {Repository} not found in configured repositories, checking environment variables", repository);
        var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            _logger.LogInformation("ResolveRemoteToken: Found token in environment variables for {Repository}", repository);
            return envToken;
        }

        _logger.LogWarning("ResolveRemoteToken: No token found for {Repository} in config or environment", repository);
        return null;
    }
}