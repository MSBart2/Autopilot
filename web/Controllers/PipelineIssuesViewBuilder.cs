using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Cyberpilot.Web.Controllers;

internal sealed class PipelineIssuesViewBuilder
{
    internal static readonly TimeSpan IssueCacheTtl = TimeSpan.FromMinutes(30);

    private readonly CyberpilotDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly RepositoryConfigurationHelper _configHelper;
    private readonly CyberpilotWebOptions _options;
    private readonly IPipelineDefinitionAdminStore _pipelineAdminStore;

    public PipelineIssuesViewBuilder(
        CyberpilotDbContext dbContext,
        IMemoryCache cache,
        RepositoryConfigurationHelper configHelper,
        CyberpilotWebOptions options,
        IPipelineDefinitionAdminStore pipelineAdminStore)
    {
        _dbContext = dbContext;
        _cache = cache;
        _configHelper = configHelper;
        _options = options;
        _pipelineAdminStore = pipelineAdminStore;
    }

    public static PipelineDashboardViewModel BuildDashboard(IReadOnlyList<PipelineRun> runs)
        => new() { Runs = runs };

    public async Task<PipelineIssuesViewModel> BuildIssuesViewModelAsync(
        IReadOnlyList<GitHubIssueSummary> issues,
        string repository,
        string repositoryInput,
        string? connectionId,
        string? error,
        CancellationToken cancellationToken = default)
    {
        var recentRuns = await _dbContext.PipelineRuns
            .AsNoTracking()
            .Where(run => run.Repository == repository)
            .OrderByDescending(run => run.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var latestRunsByIssue = recentRuns
            .GroupBy(run => run.IssueNumber)
            .Select(group => group.First())
            .ToArray();

        var sdkActiveIssueNumbers = latestRunsByIssue
            .Where(run => run.Status is "Queued" or "Running" or "Pausing")
            .Select(run => run.IssueNumber)
            .ToHashSet();

        var latestSdkRunIds = latestRunsByIssue.ToDictionary(run => run.IssueNumber, run => run.Id);

        return new PipelineIssuesViewModel(
            issues,
            [],
            repository,
            repositoryInput,
            connectionId,
            error,
            _configHelper.GetConfiguredRepositoryChoices(),
            sdkActiveIssueNumbers,
            latestSdkRunIds,
            await _pipelineAdminStore.GetDefinitionOptionsAsync(cancellationToken),
            await _pipelineAdminStore.GetPolicyOptionsAsync(cancellationToken));
    }

    public async Task<PipelineIssuesViewModel> BuildIssuesViewModelAsync(
        IReadOnlyList<GitHubIssueSummary> issues,
        IReadOnlyList<GitHubIssueSummary> pullRequests,
        string repository,
        string repositoryInput,
        string? connectionId,
        string? error,
        CancellationToken cancellationToken = default)
    {
        var recentRuns = await _dbContext.PipelineRuns
            .AsNoTracking()
            .Where(run => run.Repository == repository)
            .OrderByDescending(run => run.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var latestRunsByIssue = recentRuns
            .GroupBy(run => run.IssueNumber)
            .Select(group => group.First())
            .ToArray();

        var sdkActiveIssueNumbers = latestRunsByIssue
            .Where(run => run.Status is "Queued" or "Running" or "Pausing")
            .Select(run => run.IssueNumber)
            .ToHashSet();

        var latestSdkRunIds = latestRunsByIssue.ToDictionary(run => run.IssueNumber, run => run.Id);

        return new PipelineIssuesViewModel(
            issues,
            pullRequests,
            repository,
            repositoryInput,
            connectionId,
            error,
            _configHelper.GetConfiguredRepositoryChoices(),
            sdkActiveIssueNumbers,
            latestSdkRunIds,
            await _pipelineAdminStore.GetDefinitionOptionsAsync(cancellationToken),
            await _pipelineAdminStore.GetPolicyOptionsAsync(cancellationToken));
    }
}
