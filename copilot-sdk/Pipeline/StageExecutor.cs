namespace Cyberpilot.Pipeline;

internal sealed class StageExecutor(
    IPromptBuilder promptBuilder,
    IStageRunner stageRunner,
    StageModelResolver modelResolver,
    IStageArtifactValidator artifactValidator,
    ICyberpilotProgressSink progressSink,
    PipelineConsoleWriter console)
{
    public async Task<StageResult> RunAsync(
        PipelineStageDefinition stageDefinition,
        int issueNumber,
        TimeSpan timeout,
        string mission,
        PolicyProfile policyProfile,
        PipelineExecutionContext context,
        CancellationToken cancellationToken)
    {
        var stage = stageDefinition.Stage;
        console.WriteHeader($"Stage: {stage.DisplayName}");
        console.WriteDetail("Issue", $"#{issueNumber}");
        console.WriteDetail("Label", stage.Label);
        console.WriteDetail("Timeout", PipelineConsoleWriter.FormatDuration(timeout));

        progressSink.OnStageStarted(stage, issueNumber);
        var modelSelection = await modelResolver.ResolveAsync(stage, context, cancellationToken);
        if (!modelSelection.IsAvailable)
        {
            var unavailableResult = ApplyModelSelection(new StageResult(
                "INVALID",
                "unknown",
                false,
                modelSelection.Error,
                RequiredActions: [$"Choose an available model for stage '{stage.Name}' or configure a working fallback model."]), modelSelection);
            progressSink.OnStageCompleted(stage, unavailableResult);
            console.WriteFailure($"Stage {stage.DisplayName} model unavailable: {modelSelection.Error}");
            return unavailableResult;
        }

        if (!string.Equals(modelSelection.ConfiguredModel, modelSelection.SelectedModel, StringComparison.OrdinalIgnoreCase))
        {
            progressSink.OnDispatch(DispatchType.ModelFallback, $"Stage '{stage.Name}' using fallback model '{modelSelection.SelectedModel}' because '{modelSelection.ConfiguredModel}' is unavailable.");
        }

        var builtPrompt = await promptBuilder.BuildAsync(stageDefinition, mission, policyProfile, context, cancellationToken);
        var result = await stageRunner.RunAsync(stage, builtPrompt, timeout, modelSelection.SelectedModel, context, cancellationToken);
        result = ApplyModelSelection(result, modelSelection);
        result = AddToolArtifacts(stage.Name, result, context.GetToolArtifacts(stage.Name));
        if (!result.IsValid)
        {
            console.WriteFailure($"Stage {stage.DisplayName} returned invalid JSON result: {result.Error}");
            return result;
        }

        var validation = artifactValidator.Validate(stageDefinition, result, policyProfile);
        if (!validation.IsValid)
        {
            var invalidResult = result with
            {
                Status = "INVALID",
                IsValid = false,
                Error = validation.Error,
                RequiredActions = validation.RequiredActions,
            };
            console.WriteFailure($"Stage {stage.DisplayName} failed artifact validation: {validation.Error}");
            progressSink.OnStageCompleted(stage, invalidResult);
            return invalidResult;
        }

        console.WriteSuccess($"Stage {stage.DisplayName} complete");
        progressSink.OnStageCompleted(stage, result);
        console.WriteDetail("Status", result.Status);
        console.WriteDetail("Decision", result.Decision);
        return result;
    }

    private static StageResult AddToolArtifacts(string stageName, StageResult result, IReadOnlyList<StageArtifact> toolArtifacts)
    {
        if (toolArtifacts.Count == 0)
        {
            return result;
        }

        var existingArtifacts = result.Artifacts ?? [];
        return result with
        {
            Artifacts = existingArtifacts.Concat(toolArtifacts).ToArray(),
            Evidence = (result.Evidence ?? [])
                .Concat(toolArtifacts.Select(artifact => new StageEvidence(
                    $"tool-output:{artifact.Name}",
                    $"Deterministic tool output captured for stage '{stageName}': {artifact.Name}",
                    artifact.Uri)))
                .ToArray(),
        };
    }

    private static StageResult ApplyModelSelection(StageResult result, StageModelSelection selection)
    {
        return result with
        {
            ConfiguredModel = selection.ConfiguredModel,
            SelectedModel = selection.SelectedModel,
            FallbackModel = selection.FallbackModel,
            FallbackReason = selection.FallbackReason,
        };
    }
}
