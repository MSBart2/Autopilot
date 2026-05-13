using Cyberpilot.Copilot;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class ModelAvailabilityGateTests
{
    [Fact]
    public async Task EvaluateAsync_AvailableModel_Passes()
    {
        var checker = new RecordingModelChecker(ModelAvailabilityResult.Available);
        var gate = new ModelAvailabilityGate(checker);

        var result = await gate.EvaluateAsync(Context(model: "gpt-test", repoRoot: Directory.GetCurrentDirectory()));

        Assert.True(result.Passed);
        Assert.Contains("gpt-test", result.Summary);
        Assert.Equal("gpt-test", checker.Model);
        Assert.Equal(Directory.GetCurrentDirectory(), checker.RepoRoot);
    }

    [Fact]
    public async Task EvaluateAsync_UnavailableModel_FailsWithCorrectiveAction()
    {
        var checker = new RecordingModelChecker(ModelAvailabilityResult.Unavailable("Model is not available."));
        var gate = new ModelAvailabilityGate(checker);

        var result = await gate.EvaluateAsync(Context(model: "missing-model", repoRoot: Directory.GetCurrentDirectory()));

        Assert.False(result.Passed);
        Assert.Contains("missing-model", result.Summary);
        Assert.Contains("Model is not available", result.Summary);
        Assert.Equal(["Retry with --model <available-model-id> instead of 'missing-model'."], result.RequiredActions);
    }

    [Fact]
    public void BuiltInPipelineGates_Create_RegistersModelAvailabilityGate()
    {
        var gates = BuiltInPipelineGates.Create(new RecordingModelChecker(ModelAvailabilityResult.Available), new RecordingLabelService());

        Assert.True(gates.ContainsKey(BuiltInPipelineGates.ModelAvailable));
        Assert.IsType<ModelAvailabilityGate>(gates[BuiltInPipelineGates.ModelAvailable]);
    }

    private static PipelineGateContext Context(string model, string repoRoot)
    {
        var options = new CyberpilotOptions(1, repoRoot, "owner/repo", model, false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        var executionContext = new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition);
        var stage = new PipelineStageDefinition(
            new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage"),
            new StageContract(PipelineDefinitionDefaults.ContractVersion, []),
            [new GateDefinition(BuiltInPipelineGates.ModelAvailable, GateTiming.BeforeStage, true)]);

        return new PipelineGateContext(executionContext, stage, stage.Gates[0]);
    }

    private sealed class RecordingModelChecker(ModelAvailabilityResult result) : IModelAvailabilityChecker
    {
        public string? Model { get; private set; }

        public string? RepoRoot { get; private set; }

        public Task<ModelAvailabilityResult> CheckAsync(string model, string repoRoot, CancellationToken cancellationToken = default)
        {
            Model = model;
            RepoRoot = repoRoot;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingLabelService : Cyberpilot.GitHub.ISdkLabelService
    {
        public Task EnsureProvenanceAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureRequiredLabelsAsync(bool createMissing, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearStageAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetStageAsync(int issueNumber, string stageLabel, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
