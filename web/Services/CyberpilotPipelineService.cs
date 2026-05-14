using Cyberpilot;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Hubs;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Executes queued Cyberpilot pipeline runs in the background.
/// </summary>
public sealed class CyberpilotPipelineService(
    ICyberpilotRunQueue queue,
    IServiceScopeFactory scopeFactory,
    IHubContext<PipelineHub> hubContext,
    IOptions<CyberpilotWebOptions> options,
    ILogger<CyberpilotPipelineService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> repositoryLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedRunsAsync(stoppingToken);
        await RequeuePendingRunsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var request = await queue.DequeueAsync(stoppingToken);
            _ = ProcessWithRepositoryLockAsync(request, stoppingToken);
        }
    }

    private async Task ProcessWithRepositoryLockAsync(WebPipelineRunRequest request, CancellationToken cancellationToken)
    {
        var repositoryKey = ResolveRepoRoot(request.RepoRoot);
        var repositoryLock = repositoryLocks.GetOrAdd(repositoryKey, _ => new SemaphoreSlim(1, 1));

        try
        {
            await repositoryLock.WaitAsync(cancellationToken);
            try
            {
                await ProcessAsync(request, cancellationToken);
            }
            finally
            {
                repositoryLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Cyberpilot run {RunId} was cancelled before execution.", request.RunId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cyberpilot run {RunId} failed before entering the pipeline worker.", request.RunId);
        }
    }

    private static readonly string[] StageOrder = ["triage", "plan", "implement", "review", "docs", "deliver"];

    private static string? NextStage(string stage)
    {
        var idx = Array.FindIndex(StageOrder, s => s.Equals(stage, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 && idx < StageOrder.Length - 1 ? StageOrder[idx + 1] : null;
    }

    private async Task ProcessAsync(WebPipelineRunRequest request, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<ICyberpilotRunner>();
        var localRepositoryValidator = scope.ServiceProvider.GetRequiredService<ILocalRepositoryValidator>();
        var run = await dbContext.PipelineRuns.FirstOrDefaultAsync(item => item.Id == request.RunId, cancellationToken);
        if (run is null)
        {
            logger.LogWarning("Cyberpilot run {RunId} was queued but no longer exists.", request.RunId);
            return;
        }

        run.Status = "Running";
        await dbContext.SaveChangesAsync(cancellationToken);
        await hubContext.Clients.Group(PipelineHub.GroupName(run.Id)).SendAsync("runStarted", new { run.Id, run.IssueNumber }, cancellationToken);

        SignalRProgressSink? sink = null;
        try
        {
            var sinkLogger = scope.ServiceProvider.GetRequiredService<ILogger<SignalRProgressSink>>();
            sink = new SignalRProgressSink(run.Id, run.Model, run.IssueNumber, dbContext, hubContext, sinkLogger, request.StartStage, request.RetryReason);
            var repoRoot = await localRepositoryValidator.PrepareAsync(request.RepoRoot, request.Repository, request.GitHubToken, cancellationToken);
            run.WorktreePath = repoRoot;
            await dbContext.SaveChangesAsync(cancellationToken);
            var profileDetector = scope.ServiceProvider.GetRequiredService<IRepositoryProfileDetector>();
            var profile = await profileDetector.DetectAsync(repoRoot, cancellationToken);
            sink.OnDispatch(DispatchType.RepositoryProfile, profile.ToSummary());

            // Lambda that checks if a pause has been requested (reads fresh from DB)
            var runId = run.Id;
            async Task<PipelinePauseDecision> ShouldPauseDecisionAsync(PipelinePauseContext context, CancellationToken ct)
            {
                var current = await dbContext.PipelineRuns
                    .AsNoTracking()
                    .Where(r => r.Id == runId)
                    .Select(r => r.Status)
                    .FirstOrDefaultAsync(ct);

                if (current != "Pausing")
                {
                    return PipelinePauseDecision.Continue();
                }

                var resumeStage = NextStage(context.CompletedStageName) ?? context.CompletedStageName;
                var approvalRequest = new ApprovalGateRequest(
                    $"{runId}-{context.CompletedStageName}-operator-pause",
                    context.IssueNumber,
                    context.CompletedStageName,
                    GateTiming.AfterStage,
                    $"Operator pause requested after {context.CompletedStageName}.",
                    "operator",
                    resumeStage,
                    DateTimeOffset.UtcNow);

                return PipelinePauseDecision.Pause(
                    $"Pipeline pause requested after {context.CompletedStageName}.",
                    approvalRequest);
            }

            var result = await runner.RunAsync(new CyberpilotRunRequest(
                request.IssueNumber,
                repoRoot,
                request.Repository,
                request.GitHubToken,
                request.Model,
                request.SkipDeliver,
                request.StageTimeout,
                options.Value.ApproveAll,
                request.AllowMissingDocs,
                EnsureLabels: options.Value.EnsureLabels,
                AgentPromptRoot: request.AgentPromptRoot,
                StartStage: request.StartStage,
                ShouldPauseDecisionAsync: ShouldPauseDecisionAsync,
                PipelineDefinitionName: request.PipelineDefinitionName,
                PipelineDefinitionVersion: request.PipelineDefinitionVersion,
                PolicyProfileName: request.PolicyProfileName,
                TargetRepositoryProfileSummary: profile.ToSummary()), sink, cancellationToken);

            run.Status = result.Status;
            run.BranchName = result.BranchName;
            run.CurrentStage = result.FinalStage;
            run.Error = result.Error;
            run.PrUrl = result.PrUrl;
            run.CompletedAt = DateTime.UtcNow;

            AddPullRequestEvidence(dbContext, run.Id, result.PrUrl);

            // For paused runs, set CurrentStage to the NEXT stage so resume starts correctly
            if (result.Status == "Paused" && NextStage(result.FinalStage) is string nextStage)
            {
                run.CurrentStage = nextStage;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (result.Status == "Paused")
            {
                await hubContext.Clients.Group(PipelineHub.GroupName(run.Id)).SendAsync("runPaused", new { run.Id, pausedAfterStage = result.FinalStage, nextStage = run.CurrentStage }, cancellationToken);
            }
            else
            {
                await hubContext.Clients.Group(PipelineHub.GroupName(run.Id)).SendAsync("runCompleted", new { run.Id, run.Status, result.ExitCode, run.SkipDeliver }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cyberpilot run {RunId} failed.", run.Id);
            run.Status = "Failed";
            run.Error = ex.Message;
            run.CompletedAt = DateTime.UtcNow;

            // Finalize any stage logs still marked as Running
            var runningStageLogs = await dbContext.PipelineStageLogs
                .Where(log => log.RunId == run.Id && log.Status == "Running")
                .ToArrayAsync(CancellationToken.None);
            foreach (var stageLog in runningStageLogs)
            {
                stageLog.Status = "failed";
                stageLog.CompletedAt = DateTime.UtcNow;
            }

            // Flush any buffered output from the sink
            if (sink is not null)
            {
                await sink.FlushAsync();
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
            await hubContext.Clients.Group(PipelineHub.GroupName(run.Id)).SendAsync("runFailed", new { run.Id, error = ex.Message }, CancellationToken.None);
        }
    }

    private async Task RecoverInterruptedRunsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>();
        var interrupted = await dbContext.PipelineRuns
            .Where(run => run.Status == "Running" || run.Status == "Pausing")
            .ToArrayAsync(cancellationToken);

        foreach (var run in interrupted)
        {
            run.Status = "Failed";
            run.Error = "Run was interrupted by application restart.";
            run.CompletedAt = DateTime.UtcNow;

            // Finalize any stage logs still marked as Running
            var runningStageLogs = await dbContext.PipelineStageLogs
                .Where(log => log.RunId == run.Id && log.Status == "Running")
                .ToArrayAsync(cancellationToken);
            foreach (var stageLog in runningStageLogs)
            {
                stageLog.Status = "failed";
                stageLog.CompletedAt = DateTime.UtcNow;
            }
        }

        // Also finalize orphaned stage logs in already-completed runs (legacy data)
        var orphanedStageLogs = await dbContext.PipelineStageLogs
            .Where(log => log.Status == "Running")
            .ToArrayAsync(cancellationToken);
        foreach (var stageLog in orphanedStageLogs)
        {
            stageLog.Status = "failed";
            stageLog.CompletedAt = DateTime.UtcNow;
        }

        if (interrupted.Length > 0 || orphanedStageLogs.Length > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RequeuePendingRunsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>();
        var queuedRuns = await dbContext.PipelineRuns
            .Where(run => run.Status == "Queued")
            .OrderBy(run => run.CreatedAt)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        foreach (var run in queuedRuns)
        {
            await queue.EnqueueAsync(CreateRequest(run), cancellationToken);
        }
    }

    private WebPipelineRunRequest CreateRequest(PipelineRun run)
    {
        var repoRoot = ResolveRepoRoot(options.Value.RepoRoot);
        string? token = null;
        if (TryGetConfiguredRepository(run.Repository, out var configuredRepository))
        {
            repoRoot = configuredRepository.RepoRoot;
            token = configuredRepository.Token;
        }

        return new WebPipelineRunRequest(
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
            run.ContractVersion);
    }

    private static void AddPullRequestEvidence(CyberpilotDbContext dbContext, string runId, string? prUrl)
    {
        if (string.IsNullOrWhiteSpace(prUrl))
        {
            return;
        }

        var trimmedUrl = prUrl.Trim();
        var exists = dbContext.PipelineEvidence.Any(evidence =>
            evidence.RunId == runId
            && evidence.Kind == "pull-request-reference"
            && evidence.Uri == trimmedUrl);
        if (!exists)
        {
            dbContext.PipelineEvidence.Add(PipelineEvidence.FromPullRequest(runId, trimmedUrl));
        }
    }

    private bool TryGetConfiguredRepository(string repository, out RuntimeConfiguredRepository configuredRepository)
    {
        configuredRepository = default!;
        foreach (var option in options.Value.Repositories)
        {
            if (GitHubRepositoryParser.TryNormalize(option.Repository, out var normalizedRepository)
                && normalizedRepository.Equals(repository, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(option.Token))
            {
                configuredRepository = new RuntimeConfiguredRepository(ResolveRepoRoot(option.RepoRoot), option.Token);
                return true;
            }
        }

        return false;
    }

    private string ResolveRepoRoot(string? repoRoot)
    {
        var value = string.IsNullOrWhiteSpace(repoRoot) ? options.Value.RepoRoot : repoRoot;
        return Path.GetFullPath(value);
    }

    private string ResolveAgentPromptRoot()
    {
        var value = string.IsNullOrWhiteSpace(options.Value.AgentPromptRoot)
            ? Path.Combine(AppContext.BaseDirectory, "..")
            : options.Value.AgentPromptRoot;
        return Path.GetFullPath(value);
    }

    private sealed record RuntimeConfiguredRepository(string RepoRoot, string Token);
}
