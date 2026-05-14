using Cyberpilot.Copilot;
using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed class SdkCyberpilotRunner(
    CyberpilotOptions options,
    IGitHubIssueClient issueClient,
    ISdkLabelService labels,
    IBranchProvisioner branchProvisioner,
    IPromptBuilder promptBuilder,
    IStageRunner stageRunner,
    IModelAvailabilityChecker modelChecker,
    ICyberpilotProgressSink progressSink,
    TextWriter output)
{
    public string FinalStage => pipelineContext.FinalStage;
    public string? BranchName => pipelineContext.BranchName;
    public string? PrUrl => pipelineContext.PrUrl;
    public IReadOnlyList<StageResult> StageResults => pipelineContext.StageResults;
    private PipelineExecutionContext pipelineContext = new(options, DefaultPipelineDefinitionProvider.Definition);
    private readonly PipelineConsoleWriter console = new(output);
    private readonly StageExecutor stageExecutor = new(promptBuilder, stageRunner, new DefaultStageArtifactValidator(), progressSink, new PipelineConsoleWriter(output));
    private readonly PipelineBranchCoordinator branchCoordinator = new(options, issueClient, branchProvisioner, progressSink, new PipelineConsoleWriter(output));

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (options.CheckLabelsOnly)
        {
            console.WriteHeader("Label Preflight");
            console.WriteStep("Checking SDK labels");
            await labels.EnsureRequiredLabelsAsync(options.EnsureLabels, cancellationToken);
            console.WriteSuccess("SDK labels are ready. No stages were run.");
            return 0;
        }

        if (options.CheckModelOnly)
        {
            return await CheckModelAsync(cancellationToken);
        }

        if (!TryCreatePipelineDefinitionProvider(out var provider, out var providerError))
        {
            progressSink.OnDispatch(DispatchType.Halt, providerError ?? "Pipeline definition provider could not be loaded.");
            console.WriteFailure(providerError ?? "Pipeline definition provider could not be loaded.");
            return 12;
        }

        if (!PipelineDefinitionSelector.TrySelect(options, provider!, out var definition, out var definitionError))
        {
            progressSink.OnDispatch(DispatchType.Halt, definitionError ?? "Unsupported pipeline definition.");
            console.WriteFailure(definitionError ?? "Unsupported pipeline definition.");
            return 12;
        }

        pipelineContext = new PipelineExecutionContext(options, definition!);

        console.WriteHeader($"Cyberpilot issue #{options.IssueNumber}");
        console.WriteDetail("Repository", options.RepoRoot);
        console.WriteDetail("Model", options.Model);
        console.WriteDetail("Stage timeout", PipelineConsoleWriter.FormatDuration(options.StageTimeout));

        var issueState = await issueClient.GetIssueStateAsync(options.IssueNumber, cancellationToken);
        if (issueState.Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            progressSink.OnDispatch(DispatchType.Skip, $"Issue #{options.IssueNumber} already closed — skipping");
            console.WriteWarning($"Issue #{options.IssueNumber} is already closed. Cyberpilot will not run or modify labels.");
            return 0;
        }

        await labels.EnsureRequiredLabelsAsync(options.EnsureLabels, cancellationToken);

        if (!options.ApproveAll)
        {
            progressSink.OnDispatch(DispatchType.Halt, "Missing --approve-all flag");
            console.WriteWarning("Cyberpilot requires --approve-all before running Copilot tool requests.");
            console.WriteDetail("Pilot tip", "Run with --skip-deliver first, and only use --approve-all in a trusted repository and issue context.");
            return 10;
        }

        var modelCheckExitCode = await CheckModelAsync(cancellationToken);
        if (modelCheckExitCode != 0)
        {
            progressSink.OnDispatch(DispatchType.Halt, $"Model unavailable: {options.Model}");
            return modelCheckExitCode;
        }

        progressSink.OnDispatch(DispatchType.Preflight, $"Model verified: {options.Model}");

        await labels.EnsureProvenanceAsync(options.IssueNumber, cancellationToken);
        progressSink.OnDispatch(DispatchType.Preflight, $"Labels ready — launching pipeline for issue #{options.IssueNumber}");

        var engine = new PipelineEngine(pipelineContext, labels, branchCoordinator, stageExecutor, new PipelineGateRunner(BuiltInPipelineGates.Create(modelChecker, labels, issueClient)), progressSink, console);
        return await engine.ExecuteAsync(cancellationToken);
    }

    private bool TryCreatePipelineDefinitionProvider(out IPipelineDefinitionProvider? provider, out string? error)
    {
        var builtInProvider = new BuiltInPipelineDefinitionProvider();
        if (string.IsNullOrWhiteSpace(options.PipelineDefinitionFilePath))
        {
            provider = builtInProvider;
            error = null;
            return true;
        }

        if (!JsonPipelineDefinitionProvider.TryLoad(options.PipelineDefinitionFilePath, out var fileProvider, out error))
        {
            provider = null;
            return false;
        }

        provider = new CompositePipelineDefinitionProvider([fileProvider!, builtInProvider]);
        error = null;
        return true;
    }

    private async Task<int> CheckModelAsync(CancellationToken cancellationToken)
    {
        console.WriteHeader("Model Preflight");
        console.WriteStep($"Checking Copilot model availability: {options.Model}");
        var result = await modelChecker.CheckAsync(options.Model, options.RepoRoot, cancellationToken);
        if (result.IsAvailable)
        {
            console.WriteSuccess($"Copilot model is available: {options.Model}");
            return 0;
        }

        console.WriteFailure($"Copilot model is not available: {options.Model}");
        console.WriteDetail("SDK error", result.Error ?? "No details returned.");
        console.WriteDetail("Next step", "Retry with --model <available-model-id>.");
        return 11;
    }

}
