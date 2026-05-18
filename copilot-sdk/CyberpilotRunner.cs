using Cyberpilot.Copilot;
using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot;

/// <summary>
/// Default facade for programmatic Cyberpilot SDK runs.
/// </summary>
public sealed class CyberpilotRunner : ICyberpilotRunner
{
    private readonly Func<HttpClient> createHttpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="CyberpilotRunner" /> class.
    /// </summary>
    public CyberpilotRunner()
        : this(static () => new HttpClient())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CyberpilotRunner" /> class.
    /// </summary>
    /// <param name="createHttpClient">Creates HTTP clients for GitHub REST API-backed runs.</param>
    public CyberpilotRunner(Func<HttpClient> createHttpClient)
    {
        this.createHttpClient = createHttpClient ?? throw new ArgumentNullException(nameof(createHttpClient));
    }

    /// <inheritdoc />
    public Task<CyberpilotRunResult> RunAsync(CyberpilotRunRequest request, CancellationToken cancellationToken = default)
    {
        return RunAsync(request, new TextWriterProgressSink(TextWriter.Null, TextWriter.Null), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CyberpilotRunResult> RunAsync(CyberpilotRunRequest request, ICyberpilotProgressSink progressSink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progressSink);

        var issueClient = CreateIssueClient(request);
        var output = TextWriter.Null;
        var labels = new SdkLabelService(issueClient, output);
        var branchProvisioner = new BranchProvisioner();
        var promptBuilder = new PromptBuilder(request.RepoRoot, request.AgentPromptRoot ?? request.RepoRoot, request.IssueNumber, request.TargetRepositoryProfileSummary, request.RuntimePreferences);
        var stageRunner = new CopilotStageRunner(request.RepoRoot, progressSink, TextWriter.Null);
        var modelChecker = new CopilotModelAvailabilityChecker();
        var options = new CyberpilotOptions(
            request.IssueNumber,
            request.RepoRoot,
            request.Repository,
            request.Model,
            request.SkipDeliver,
            request.EnsureLabels,
            false,
            false,
            request.StageTimeout,
            request.ApproveAll,
            request.AllowMissingDocs,
            null,
            null,
            false,
            request.StartStage,
            request.ShouldPauseAsync,
            request.PipelineDefinitionName ?? PipelineDefinitionDefaults.DefinitionName,
            request.PipelineDefinitionVersion ?? PipelineDefinitionDefaults.DefinitionVersion,
            request.PolicyProfileName ?? PipelineDefinitionDefaults.PolicyProfileName,
            request.ShouldPauseDecisionAsync,
            request.PipelineDefinitionFilePath,
            request.PrHeadBranch,
            request.AgentPromptRoot,
            request.StageModelOverrides,
            request.StageModelFallbacks,
            RuntimePreferences: request.RuntimePreferences);

        var runner = new SdkCyberpilotRunner(options, issueClient, labels, branchProvisioner, promptBuilder, stageRunner, modelChecker, progressSink, output);
        var exitCode = await runner.RunAsync(cancellationToken);
        var status = exitCode switch
        {
            0 => "Completed",
            2 => "Stopped",
            3 => "Paused",
            _ => "Failed",
        };
        return CyberpilotRunResult.FromExitCode(exitCode, runner.FinalStage, status, runner.BranchName, runner.PrUrl, stageResults: runner.StageResults);
    }

    private IGitHubIssueClient CreateIssueClient(CyberpilotRunRequest request)
    {
        var token = request.GitHubToken ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(request.Repository))
        {
            return new GitHubApiIssueClient(createHttpClient(), request.Repository, token);
        }

        return new GitHubIssueClient(new GitHubCli(request.RepoRoot, request.Repository));
    }
}
