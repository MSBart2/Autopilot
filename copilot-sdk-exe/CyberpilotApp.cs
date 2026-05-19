using Cyberpilot.Copilot;
using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace Cyberpilot;

/// <summary>
/// Console application entry point wrapper for the Cyberpilot SDK runner.
/// </summary>
public sealed class CyberpilotApp(TextWriter output, TextWriter error)
{
    /// <summary>
    /// Runs the console harness with command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>A process-style exit code.</returns>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = CyberpilotOptions.Parse(args);
            if (options.ShowHelp)
            {
                output.WriteLine(CyberpilotOptions.HelpText);
                return 0;
            }

            var configuration = SdkConfiguration.Load(options.ConfigPath, options.RepoRoot);
            options = configuration.ApplyTo(options);

            var issueClient = CreateIssueClient(options, configuration);
            await using var dbContext = await CreateDbContextAsync(options, cancellationToken);

            if (options.ResetMode)
            {
                return await RunResetAsync(options, issueClient, dbContext, cancellationToken);
            }

            if (options.BenchmarkRepeat > 1)
            {
                return await RunBenchmarkLoopAsync(options, issueClient, dbContext, cancellationToken);
            }

            var labels = new SdkLabelService(issueClient, output);
            var run = dbContext is null ? null : await CreateRunAsync(dbContext, options, cancellationToken);
            var branchProvisioner = new BranchProvisioner();
            var progressSink = CreateProgressSink(dbContext, run, options);
            var promptBuilder = new PromptBuilder(options.RepoRoot, options.AgentPromptRoot ?? options.RepoRoot, options.IssueNumber, runtimePreferences: options.RuntimePreferences);
            var stageRunner = new CopilotStageRunner(options.RepoRoot, progressSink, error);
            var modelChecker = new CopilotModelAvailabilityChecker();
            var runner = new SdkCyberpilotRunner(options, issueClient, labels, branchProvisioner, promptBuilder, stageRunner, modelChecker, progressSink, output);
            var exitCode = await runner.RunAsync(cancellationToken);
            if (dbContext is not null && run is not null)
            {
                run.Status = exitCode switch
                {
                    0 => "Completed",
                    2 => "Stopped",
                    _ => "Failed",
                };
                run.CurrentStage = runner.FinalStage;
                run.BranchName = runner.BranchName;
                run.CompletedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            error.WriteLine($"SDK cyberpilot failed: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> RunResetAsync(CyberpilotOptions options, IGitHubIssueClient issueClient, CyberpilotDbContext? dbContext, CancellationToken cancellationToken)
    {
        var resetService = new PipelineResetService(issueClient, dbContext);
        PipelineResetResult result;

        if (options.IssueNumber > 0)
        {
            output.WriteLine($"Resetting issue #{options.IssueNumber}...");
            result = await resetService.ResetIssueAsync(options.IssueNumber, options.RepoRoot, cancellationToken: cancellationToken);
        }
        else
        {
            error.WriteLine("Reset requires an issue number. Try: reset issue <number>");
            return 1;
        }

        output.WriteLine(result.ToSummary());
        return 0;
    }

    private async Task<int> RunBenchmarkLoopAsync(CyberpilotOptions options, IGitHubIssueClient issueClient, CyberpilotDbContext? dbContext, CancellationToken cancellationToken)
    {
        var stage = options.OnlyStage ?? "pipeline";
        var modeLabel = options.RuntimePreferences?.SystemMessageMode switch
        {
            HarnessSystemMessageMode.Append => "append",
            HarnessSystemMessageMode.Replace => "replace",
            _ => "none",
        };
        var variantBase = options.ExperimentVariant ?? $"{modeLabel}-{stage}";
        var repeatGroup = Guid.NewGuid().ToString("N")[..12];

        output.WriteLine($"============================================================");
        output.WriteLine($"Benchmark: issue #{options.IssueNumber} | stage: {stage} | mode: {modeLabel} | repeat: {options.BenchmarkRepeat}x | group: {repeatGroup}");
        output.WriteLine($"============================================================");

        var resetService = new PipelineResetService(issueClient, dbContext);
        var results = new List<(int Iteration, string Status, long? InputTokens, long? DurationMs, int? ToolCalls, int? FailedTools)>();

        for (var iteration = 1; iteration <= options.BenchmarkRepeat; iteration++)
        {
            if (iteration > 1)
            {
                output.WriteLine($"\n[benchmark] Resetting issue #{options.IssueNumber} before iteration {iteration}...");
                var resetResult = await resetService.ResetIssueAsync(options.IssueNumber, options.RepoRoot, cancellationToken: cancellationToken);
                output.WriteLine($"[benchmark] {resetResult.ToSummary()}");
            }

            output.WriteLine($"\n============================================================");
            output.WriteLine($"Iteration {iteration} of {options.BenchmarkRepeat} — variant: {variantBase}-iter-{iteration}");
            output.WriteLine($"============================================================");

            var labels = new SdkLabelService(issueClient, output);
            var variant = $"{variantBase}-iter-{iteration}";
            var run = dbContext is null ? null : await CreateRunAsync(dbContext, options, cancellationToken, variant, iteration, repeatGroup);
            var branchProvisioner = new BranchProvisioner();
            var progressSink = CreateProgressSink(dbContext, run, options);
            var promptBuilder = new PromptBuilder(options.RepoRoot, options.AgentPromptRoot ?? options.RepoRoot, options.IssueNumber, runtimePreferences: options.RuntimePreferences);
            var stageRunner = new CopilotStageRunner(options.RepoRoot, progressSink, error);
            var modelChecker = new CopilotModelAvailabilityChecker();
            var runner = new SdkCyberpilotRunner(options, issueClient, labels, branchProvisioner, promptBuilder, stageRunner, modelChecker, progressSink, output);
            var exitCode = await runner.RunAsync(cancellationToken);

            if (dbContext is not null && run is not null)
            {
                run.Status = exitCode switch { 0 => "Completed", 2 => "Stopped", _ => "Failed" };
                run.CurrentStage = runner.FinalStage;
                run.BranchName = runner.BranchName;
                run.CompletedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                var stageLog = dbContext.Set<PipelineStageLog>()
                    .Where(s => s.RunId == run.Id)
                    .OrderBy(s => s.StartedAt)
                    .FirstOrDefault(s => s.StageName.Equals(stage, StringComparison.OrdinalIgnoreCase));
                results.Add((iteration, run.Status, stageLog?.InputTokens, (long?)stageLog?.DurationMs, stageLog?.ToolCallCount, stageLog?.FailedToolCallCount));
            }
            else
            {
                results.Add((iteration, exitCode == 0 ? "Completed" : "Failed", null, null, null, null));
            }
        }

        output.WriteLine($"\n============================================================");
        output.WriteLine($"Benchmark Summary — {variantBase} (group: {repeatGroup})");
        output.WriteLine($"============================================================");
        output.WriteLine($"{"Iter",-6} {"Status",-12} {"InputTokens",-14} {"Duration",-12} {"Tools",-8} {"Failed",-8}");
        output.WriteLine(new string('-', 62));
        foreach (var (iter, status, tokens, duration, tools, failed) in results)
        {
            output.WriteLine($"{iter,-6} {status,-12} {tokens?.ToString() ?? "n/a",-14} {(duration.HasValue ? $"{duration / 1000.0:F1}s" : "n/a"),-12} {tools?.ToString() ?? "n/a",-8} {failed?.ToString() ?? "n/a",-8}");
        }

        return 0;
    }

    private static IGitHubIssueClient CreateIssueClient(CyberpilotOptions options, SdkConfiguration configuration)    {
        var token = configuration.GetToken(options.Repository)
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(options.Repository))
        {
            return new GitHubApiIssueClient(new HttpClient(), options.Repository, token);
        }

        var cli = new GitHubCli(options.RepoRoot, options.Repository);
        return new GitHubIssueClient(cli);
    }

    private static async Task<CyberpilotDbContext?> CreateDbContextAsync(CyberpilotOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.DatabaseConnectionString))
        {
            return null;
        }

        if (!options.ResetMode && options.IssueNumber <= 0)
        {
            return null;
        }

        var dbOptions = new DbContextOptionsBuilder<CyberpilotDbContext>()
            .UseSqlite(options.DatabaseConnectionString)
            .Options;
        var dbContext = new CyberpilotDbContext(dbOptions);
        await dbContext.Database.MigrateAsync(cancellationToken);
        return dbContext;
    }

    private static async Task<PipelineRun> CreateRunAsync(CyberpilotDbContext dbContext, CyberpilotOptions options, CancellationToken cancellationToken,
        string? experimentVariant = null, int? benchmarkIteration = null, string? benchmarkRepeatGroup = null)
    {
        var targetRepoSha = await GitRevParser.TryGetHeadShaAsync(options.RepoRoot, cancellationToken);
        var cyberpilotRoot = GitRevParser.FindGitRoot(AppContext.BaseDirectory);
        var cyberpilotSha = cyberpilotRoot is not null
            ? await GitRevParser.TryGetHeadShaAsync(cyberpilotRoot, cancellationToken)
            : null;

        var run = new PipelineRun
        {
            IssueNumber = options.IssueNumber,
            Repository = options.Repository ?? string.Empty,
            Model = options.Model,
            Status = "Running",
            TriggeredBy = Environment.UserName,
            SkipDeliver = options.SkipDeliver,
            StageTimeoutMinutes = options.StageTimeout.TotalMinutes,
            AllowMissingDocs = options.AllowMissingDocs,
            PipelineDefinitionName = options.PipelineDefinitionName,
            PipelineDefinitionVersion = options.PipelineDefinitionVersion,
            PolicyProfileName = options.PolicyProfileName,
            ContractVersion = PipelineDefinitionDefaults.ContractVersion,
            TargetRepoSha = targetRepoSha,
            CyberpilotSha = cyberpilotSha,
            ExperimentVariant = experimentVariant ?? options.ExperimentVariant,
            BenchmarkIteration = benchmarkIteration,
            BenchmarkRepeatGroup = benchmarkRepeatGroup,
        };

        dbContext.PipelineRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    private ICyberpilotProgressSink CreateProgressSink(CyberpilotDbContext? dbContext, PipelineRun? run, CyberpilotOptions options)
    {
        var terminalSink = new TextWriterProgressSink(output, error);
        if (dbContext is null || run is null)
        {
            return terminalSink;
        }

        return new CompositeProgressSink(terminalSink, new CyberpilotRunHistoryProgressSink(run.Id, run.Model, dbContext));
    }
}
