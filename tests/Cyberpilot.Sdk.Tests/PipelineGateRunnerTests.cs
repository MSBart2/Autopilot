using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class PipelineGateRunnerTests
{
    [Fact]
    public async Task RunAsync_NoDeclaredGates_ReturnsNoEvaluations()
    {
        var runner = PipelineGateRunner.Empty;
        var stage = Stage([]);

        var evaluations = await runner.RunAsync(Context(), stage, GateTiming.BeforeStage);

        Assert.Empty(evaluations);
    }

    [Fact]
    public async Task RunAsync_DeclaredGate_EvaluatesMatchingTiming()
    {
        var gate = new RecordingGate(PipelineGateResult.Pass("ready"));
        var runner = new PipelineGateRunner(new Dictionary<string, IPipelineGate>(StringComparer.OrdinalIgnoreCase)
        {
            ["branch-writable"] = gate,
        });
        var stage = Stage([new GateDefinition("branch-writable", GateTiming.BeforeStage, true)]);

        var evaluations = await runner.RunAsync(Context(), stage, GateTiming.BeforeStage);

        var evaluation = Assert.Single(evaluations);
        Assert.True(evaluation.Result.Passed);
        Assert.Equal("ready", evaluation.Result.Summary);
        Assert.Equal("implement", gate.LastContext?.Stage.Stage.Name);
    }

    [Fact]
    public async Task RunAsync_MissingEvaluator_ReturnsFailedEvaluation()
    {
        var runner = PipelineGateRunner.Empty;
        var stage = Stage([new GateDefinition("missing-gate", GateTiming.AfterStage, true)]);

        var evaluations = await runner.RunAsync(Context(), stage, GateTiming.AfterStage);

        var evaluation = Assert.Single(evaluations);
        Assert.False(evaluation.Result.Passed);
        Assert.Contains("no evaluator is registered", evaluation.Result.Summary);
        Assert.Equal(["Register a deterministic evaluator for gate 'missing-gate'."], evaluation.Result.RequiredActions);
    }

    private static PipelineExecutionContext Context()
    {
        var options = new CyberpilotOptions(1, Directory.GetCurrentDirectory(), "owner/repo", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        return new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition);
    }

    private static PipelineStageDefinition Stage(IReadOnlyList<GateDefinition> gates)
        => new(
            new StageDefinition("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing"),
            new StageContract(PipelineDefinitionDefaults.ContractVersion, []),
            gates);

    private sealed class RecordingGate(PipelineGateResult result) : IPipelineGate
    {
        public PipelineGateContext? LastContext { get; private set; }

        public Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            return Task.FromResult(result);
        }
    }
}
