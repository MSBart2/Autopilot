using Cyberpilot.Copilot;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class StageModelResolverTests
{
    [Fact]
    public async Task ResolveAsync_WithAvailableStageOverride_SelectsOverride()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpt-4.1" });
        var resolver = new StageModelResolver(CreateOptions(stageModels: new Dictionary<string, string> { ["review"] = "gpt-4.1" }), checker);

        var selection = await resolver.ResolveAsync(Stage("review"), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("gpt-4.1", selection.ConfiguredModel);
        Assert.Equal("gpt-4.1", selection.SelectedModel);
        Assert.Null(selection.FallbackModel);
    }

    [Fact]
    public async Task ResolveAsync_WithUnavailableOverride_UsesConfiguredFallback()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude-haiku-4.5" });
        var resolver = new StageModelResolver(CreateOptions(
            stageModels: new Dictionary<string, string> { ["review"] = "gpt-4.1" },
            fallbackModels: new Dictionary<string, string> { ["review"] = "claude-haiku-4.5" }), checker);

        var selection = await resolver.ResolveAsync(Stage("review"), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("gpt-4.1", selection.ConfiguredModel);
        Assert.Equal("claude-haiku-4.5", selection.SelectedModel);
        Assert.Equal("claude-haiku-4.5", selection.FallbackModel);
        Assert.Contains("gpt-4.1 unavailable", selection.FallbackReason);
    }

    [Fact]
    public async Task ResolveAsync_WithUnavailableOverrideAndFallback_ReturnsUnavailable()
    {
        var resolver = new StageModelResolver(CreateOptions(
            stageModels: new Dictionary<string, string> { ["review"] = "gpt-4.1" },
            fallbackModels: new Dictionary<string, string> { ["review"] = "claude-haiku-4.5" }), new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        var selection = await resolver.ResolveAsync(Stage("review"), null, CancellationToken.None);

        Assert.False(selection.IsAvailable);
        Assert.Equal("gpt-4.1", selection.ConfiguredModel);
        Assert.Equal("claude-haiku-4.5", selection.FallbackModel);
        Assert.Contains("Fallback 'claude-haiku-4.5' is also unavailable", selection.Error);
    }

    [Theory]
    [InlineData("triage")]
    [InlineData("docs")]
    [InlineData("deliver")]
    public async Task ResolveAsync_ForSmallClaudeStage_SelectsHaikuWithinFamily(string stageName)
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude-haiku-4.5" });
        var resolver = new StageModelResolver(CreateOptions(), checker);

        var selection = await resolver.ResolveAsync(Stage(stageName), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("claude-haiku-4.5", selection.ConfiguredModel);
        Assert.Equal("claude-haiku-4.5", selection.SelectedModel);
        Assert.Null(selection.FallbackModel);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("implement")]
    [InlineData("review")]
    public async Task ResolveAsync_ForMediumClaudeStage_SelectsSonnetWithinFamily(string stageName)
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude-sonnet-4.6" });
        var resolver = new StageModelResolver(CreateOptions(), checker);

        var selection = await resolver.ResolveAsync(Stage(stageName), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("claude-sonnet-4.6", selection.ConfiguredModel);
        Assert.Equal("claude-sonnet-4.6", selection.SelectedModel);
    }

    [Fact]
    public async Task ResolveAsync_ForUnavailableAutoTieredModel_FallsBackToBaseModel()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude-sonnet-4.6" });
        var resolver = new StageModelResolver(CreateOptions(), checker);

        var selection = await resolver.ResolveAsync(Stage("triage"), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("claude-haiku-4.5", selection.ConfiguredModel);
        Assert.Equal("claude-sonnet-4.6", selection.SelectedModel);
        Assert.Equal("claude-sonnet-4.6", selection.FallbackModel);
        Assert.Contains("claude-haiku-4.5 unavailable", selection.FallbackReason);
    }

    [Fact]
    public async Task ResolveAsync_ForCheapGptStage_SelectsMiniWithinFamily()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpt-5-mini" });
        var resolver = new StageModelResolver(CreateOptions(model: "gpt-5.4"), checker);

        var selection = await resolver.ResolveAsync(Stage("docs"), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("gpt-5-mini", selection.ConfiguredModel);
        Assert.Equal("gpt-5-mini", selection.SelectedModel);
    }

    [Fact]
    public async Task ResolveAsync_WithPriorLargeRecommendation_EscalatesImplementWithinFamily()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude-opus-4.6" });
        var resolver = new StageModelResolver(CreateOptions(), checker);
        var context = CreateContext();
        context.RecordStageResult("plan", new StageResult("GO", "approved", true, null, RecommendedModelTier: "large"));

        var selection = await resolver.ResolveAsync(Stage("implement"), context, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("claude-opus-4.6", selection.ConfiguredModel);
        Assert.Equal("claude-opus-4.6", selection.SelectedModel);
    }

    [Fact]
    public async Task ResolveAsync_WithPriorSmallRecommendation_DoesNotDowngradeImplement()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude-sonnet-4.6" });
        var resolver = new StageModelResolver(CreateOptions(), checker);
        var context = CreateContext();
        context.RecordStageResult("plan", new StageResult("GO", "approved", true, null, RecommendedModelTier: "small"));

        var selection = await resolver.ResolveAsync(Stage("implement"), context, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("claude-sonnet-4.6", selection.ConfiguredModel);
        Assert.Equal("claude-sonnet-4.6", selection.SelectedModel);
    }

    [Fact]
    public async Task ResolveAsync_WithStageOverride_IgnoresPriorRecommendation()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpt-4.1" });
        var resolver = new StageModelResolver(CreateOptions(stageModels: new Dictionary<string, string> { ["implement"] = "gpt-4.1" }), checker);
        var context = CreateContext();
        context.RecordStageResult("plan", new StageResult("GO", "approved", true, null, RecommendedModelTier: "large"));

        var selection = await resolver.ResolveAsync(Stage("implement"), context, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("gpt-4.1", selection.ConfiguredModel);
        Assert.Equal("gpt-4.1", selection.SelectedModel);
    }

    [Fact]
    public async Task ResolveAsync_WithLargeBaseModel_DoesNotDowngradeImplement()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude-opus-4.6" });
        var resolver = new StageModelResolver(CreateOptions(model: "claude-opus-4.6"), checker);

        var selection = await resolver.ResolveAsync(Stage("implement"), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("claude-opus-4.6", selection.ConfiguredModel);
        Assert.Equal("claude-opus-4.6", selection.SelectedModel);
    }

    [Fact]
    public async Task ResolveAsync_ForReviewDimension_UsesReviewTier()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude-sonnet-4.6" });
        var resolver = new StageModelResolver(CreateOptions(), checker);

        var selection = await resolver.ResolveAsync(Stage("review:security"), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("claude-sonnet-4.6", selection.ConfiguredModel);
        Assert.Equal("claude-sonnet-4.6", selection.SelectedModel);
    }

    [Fact]
    public async Task ResolveAsync_ForReviewDimension_UsesReviewStageOverride()
    {
        var checker = new FakeModelAvailabilityChecker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpt-4.1" });
        var resolver = new StageModelResolver(CreateOptions(stageModels: new Dictionary<string, string> { ["review"] = "gpt-4.1" }), checker);

        var selection = await resolver.ResolveAsync(Stage("review:security"), null, CancellationToken.None);

        Assert.True(selection.IsAvailable);
        Assert.Equal("gpt-4.1", selection.ConfiguredModel);
        Assert.Equal("gpt-4.1", selection.SelectedModel);
    }

    private static CyberpilotOptions CreateOptions(
        string model = "claude-sonnet-4.6",
        IReadOnlyDictionary<string, string>? stageModels = null,
        IReadOnlyDictionary<string, string>? fallbackModels = null)
    {
        return new CyberpilotOptions(
            42,
            Directory.GetCurrentDirectory(),
            "owner/repo",
            model,
            false,
            false,
            false,
            false,
            CyberpilotOptions.DefaultStageTimeout,
            true,
            false,
            null,
            null,
            false,
            StageModelOverrides: stageModels,
            StageModelFallbacks: fallbackModels);
    }

    private static StageDefinition Stage(string name)
        => new(name.ToUpperInvariant(), name, $"{name}.agent.md", $"sdk/{name}");

    private static PipelineExecutionContext CreateContext()
    {
        var stage = Stage("implement");
        var definition = new PipelineDefinition(
            "test",
            new PipelineDefinitionVersion("1.0"),
            new PolicyProfile("standard", PolicyStrictness.Standard),
            [new PipelineStageDefinition(stage, new StageContract("1.0", []), [])],
            []);
        return new PipelineExecutionContext(CreateOptions(), definition);
    }

    private sealed class FakeModelAvailabilityChecker(IReadOnlySet<string> availableModels) : IModelAvailabilityChecker
    {
        public Task<ModelAvailabilityResult> CheckAsync(string model, string repoRoot, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(availableModels.Contains(model)
                ? ModelAvailabilityResult.Available
                : ModelAvailabilityResult.Unavailable($"{model} unavailable"));
        }
    }
}
