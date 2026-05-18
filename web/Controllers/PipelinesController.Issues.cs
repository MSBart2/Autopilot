using Cyberpilot.GitHub;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Cyberpilot.Web.Controllers;

public partial class PipelinesController
{
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

            var issuesTask = _cache.GetOrCreateAsync(
                $"issues:list:{_options.Repository}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = PipelineIssuesViewBuilder.IssueCacheTtl;
                    return await _issueClient.ListOpenIssuesAsync(HttpContext.RequestAborted);
                });
            var pullRequestsTask = _cache.GetOrCreateAsync(
                $"prs:list:{_options.Repository}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = PipelineIssuesViewBuilder.IssueCacheTtl;
                    return await _issueClient.ListOpenPullRequestsAsync(HttpContext.RequestAborted);
                });
            await Task.WhenAll(issuesTask, pullRequestsTask);
            var issues = await issuesTask ?? [];
            var pullRequests = await pullRequestsTask ?? [];
            return View(await _viewBuilder.BuildIssuesViewModelAsync(issues, pullRequests, _options.Repository, _options.Repository, null, null, HttpContext.RequestAborted));
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

        var run = new Cyberpilot.Persistence.PipelineRun
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

        await EnqueueRunAsync(
            run,
            repoRoot,
            connection?.Token,
            stageModelOverrides: ParseStageModels(request.StageModelOverrides),
            stageModelFallbacks: ParseStageModels(request.StageModelFallbacks));

        return RedirectToAction(nameof(Details), new { id = run.Id });
    }

    private static IReadOnlyDictionary<string, string>? ParseStageModels(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = item.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == item.Length - 1)
            {
                continue;
            }

            var stageName = item[..separator].Trim();
            var model = item[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(stageName) && !string.IsNullOrWhiteSpace(model))
            {
                models[stageName] = model;
            }
        }

        return models.Count == 0 ? null : models;
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
    /// Starts a review → docs → deliver run for an open pull request.
    /// </summary>
    /// <param name="prNumber">The pull request number.</param>
    /// <param name="request">The PR review start request.</param>
    /// <returns>A redirect to the run details page.</returns>
    [HttpPost("PRs/{prNumber:int}/Start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartPrReview(int prNumber, PrReviewStartRequest request)
    {
        request.PrNumber = prNumber;

        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.HeadBranch))
        {
            TempData["PipelineError"] = "Pull request review request was invalid.";
            return RedirectToAction(nameof(Issues));
        }

        if (!GitHubRepositoryParser.TryNormalize(request.Repository, out var repository))
        {
            TempData["PipelineError"] = "Pull request review request had an invalid repository.";
            return RedirectToAction(nameof(Issues));
        }

        var hasActiveRun = await _dbContext.PipelineRuns.AnyAsync(run =>
            run.Repository == repository && run.IssueNumber == prNumber && (run.Status == "Queued" || run.Status == "Running" || run.Status == "Pausing"));
        if (hasActiveRun)
        {
            TempData["PipelineError"] = $"{repository} PR #{prNumber} already has an active Cyberpilot run.";
            return RedirectToAction(nameof(Issues));
        }

        var connection = _connectionStore.Get(request.ConnectionId);
        var (repoRoot, token) = ResolveRepoConfig(repository);
        if (connection is not null && connection.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase))
        {
            token = connection.Token;
            repoRoot = connection.RepoRoot;
        }

        BuiltInPipelineCatalog.TryGetDefinition(PipelineDefinitionDefaults.DefinitionName, out var definition);
        BuiltInPipelineCatalog.TryGetPolicyProfile(PipelineDefinitionDefaults.PolicyProfileName, out var policyProfile);

        var run = new Cyberpilot.Persistence.PipelineRun
        {
            IssueNumber = prNumber,
            Repository = repository,
            Model = request.Model,
            Status = "Queued",
            TriggeredBy = User.Identity?.Name,
            SkipDeliver = false,
            StageTimeoutMinutes = request.StageTimeoutMinutes,
            AllowMissingDocs = false,
            IssueTitle = $"PR #{prNumber}: {request.HeadBranch}",
            BranchName = request.HeadBranch,
            CurrentStage = "review",
            PipelineDefinitionName = definition?.Name ?? PipelineDefinitionDefaults.DefinitionName,
            PipelineDefinitionVersion = definition?.Version ?? PipelineDefinitionDefaults.DefinitionVersion,
            PolicyProfileName = policyProfile?.Name ?? PipelineDefinitionDefaults.PolicyProfileName,
            ContractVersion = PipelineDefinitionDefaults.ContractVersion,
        };

        _dbContext.PipelineRuns.Add(run);
        await _dbContext.SaveChangesAsync();

        await EnqueueRunAsync(run, repoRoot, token, prHeadBranch: request.HeadBranch);

        TempData["PipelineNotice"] = $"Review → Docs → Deliver started for PR #{prNumber} on branch {request.HeadBranch}.";
        return RedirectToAction(nameof(Details), new { id = run.Id });
    }
}
