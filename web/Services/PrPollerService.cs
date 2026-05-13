using Cyberpilot.Persistence;
using Cyberpilot.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Background service that polls target repositories for pull requests
/// linked to issues on remote runs awaiting Copilot completion.
/// </summary>
public sealed class PrPollerService(
    IServiceScopeFactory scopeFactory,
    IHubContext<PipelineHub> hubContext,
    IHttpClientFactory httpClientFactory,
    IOptions<CyberpilotWebOptions> options,
    ILogger<PrPollerService> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BackoffInterval = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit on startup to let the app settle
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DefaultPollInterval;
            try
            {
                await PollForPullRequestsAsync(stoppingToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                logger.LogWarning("GitHub API rate limit hit during PR polling. Backing off.");
                delay = BackoffInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PR poller encountered an unexpected error.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task PollForPullRequestsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>();

        var awaitingRuns = await dbContext.PipelineRuns
            .Where(r => r.IsRemote && r.Status == "AwaitingCopilot" && r.PrUrl == null)
            .ToArrayAsync(ct);

        if (awaitingRuns.Length == 0)
        {
            return;
        }

        logger.LogDebug("Polling {Count} remote run(s) for PR discovery.", awaitingRuns.Length);

        foreach (var run in awaitingRuns)
        {
            var targetRepo = run.TargetRepository ?? run.Repository;
            var token = ResolveToken(targetRepo);

            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogDebug("No token available for {Repository}, skipping PR poll.", targetRepo);
                continue;
            }

            var prUrl = await FindLinkedPullRequestAsync(targetRepo, run.IssueNumber, token, ct);
            if (prUrl is not null)
            {
                run.PrUrl = prUrl;
                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Discovered PR {PrUrl} for remote run {RunId} (issue #{IssueNumber} on {Repository}).",
                    prUrl, run.Id, run.IssueNumber, targetRepo);

                await hubContext.Clients
                    .Group(PipelineHub.GroupName(run.Id))
                    .SendAsync("prDiscovered", new { run.Id, prUrl }, ct);
            }
        }
    }

    private async Task<string?> FindLinkedPullRequestAsync(string repository, int issueNumber, string token, CancellationToken ct)
    {
        var parts = repository.Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        var owner = parts[0];
        var repo = parts[1];

        using var client = httpClientFactory.CreateClient("GitHubApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("Cyberpilot-PrPoller");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Strategy 1: Timeline cross-references
        var timelineUrl = $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}/timeline?per_page=100";
        var response = await client.GetAsync(timelineUrl, ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Timeline API returned {StatusCode} for {Owner}/{Repo}#{IssueNumber}.",
                response.StatusCode, owner, repo, issueNumber);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        foreach (var evt in doc.RootElement.EnumerateArray())
        {
            if (evt.TryGetProperty("event", out var eventProp)
                && eventProp.GetString() == "cross-referenced"
                && evt.TryGetProperty("source", out var source)
                && source.TryGetProperty("issue", out var issue)
                && issue.TryGetProperty("pull_request", out _)
                && issue.TryGetProperty("html_url", out var htmlUrl))
            {
                var prHtmlUrl = htmlUrl.GetString();
                if (!string.IsNullOrWhiteSpace(prHtmlUrl))
                {
                    return prHtmlUrl;
                }
            }
        }

        // Strategy 2: Search open PRs that mention the issue via closing keywords
        var pullsUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls?state=open&sort=updated&direction=desc&per_page=30";
        response = await client.GetAsync(pullsUrl, ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        json = await response.Content.ReadAsStringAsync(ct);
        using var pullsDoc = JsonDocument.Parse(json);

        var pattern = $"#{issueNumber}";
        foreach (var pr in pullsDoc.RootElement.EnumerateArray())
        {
            var body = pr.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            var title = pr.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";

            if (body.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                || title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                if (pr.TryGetProperty("html_url", out var prUrlProp))
                {
                    return prUrlProp.GetString();
                }
            }
        }

        return null;
    }

    private string? ResolveToken(string repository)
    {
        var configured = options.Value.Repositories
            .FirstOrDefault(r => r.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(configured?.Token))
        {
            return configured.Token;
        }

        return Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN");
    }
}
