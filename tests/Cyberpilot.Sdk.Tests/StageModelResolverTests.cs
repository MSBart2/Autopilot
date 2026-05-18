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

        var selection = await resolver.ResolveAsync(Stage("review"), CancellationToken.None);

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

        var selection = await resolver.ResolveAsync(Stage("review"), CancellationToken.None);

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

        var selection = await resolver.ResolveAsync(Stage("review"), CancellationToken.None);

        Assert.False(selection.IsAvailable);
        Assert.Equal("gpt-4.1", selection.ConfiguredModel);
        Assert.Equal("claude-haiku-4.5", selection.FallbackModel);
        Assert.Contains("Fallback 'claude-haiku-4.5' is also unavailable", selection.Error);
    }

    private static CyberpilotOptions CreateOptions(
        IReadOnlyDictionary<string, string>? stageModels = null,
        IReadOnlyDictionary<string, string>? fallbackModels = null)
    {
        return new CyberpilotOptions(
            42,
            Directory.GetCurrentDirectory(),
            "owner/repo",
            "claude-sonnet-4.6",
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