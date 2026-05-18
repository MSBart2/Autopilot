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
            var labels = new SdkLabelService(issueClient, output);
            await using var dbContext = await CreateDbContextAsync(options, cancellationToken);
            var run = dbContext is null ? null : await CreateRunAsync(dbContext, options, cancellationToken);
            var branchProvisioner = new BranchProvisioner();
            var progressSink = CreateProgressSink(dbContext, run, options);
            var promptBuilder = new PromptBuilder(options.RepoRoot, options.RepoRoot, options.IssueNumber);
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

    private static IGitHubIssueClient CreateIssueClient(CyberpilotOptions options, SdkConfiguration configuration)
    {
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
        if (string.IsNullOrWhiteSpace(options.DatabaseConnectionString) || options.IssueNumber <= 0)
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

    private static async Task<PipelineRun> CreateRunAsync(CyberpilotDbContext dbContext, CyberpilotOptions options, CancellationToken cancellationToken)
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
