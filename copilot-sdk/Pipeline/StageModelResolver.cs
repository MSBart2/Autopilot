using Cyberpilot.Copilot;
using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed class StageModelResolver(CyberpilotOptions options, IModelAvailabilityChecker modelChecker)
{
    public async Task<StageModelSelection> ResolveAsync(StageDefinition stage, CancellationToken cancellationToken)
    {
        var configuredModel = ResolveConfiguredModel(stage.Name);
        var configuredAvailability = await modelChecker.CheckAsync(configuredModel, options.RepoRoot, cancellationToken);
        if (configuredAvailability.IsAvailable)
        {
            return StageModelSelection.Selected(configuredModel);
        }

        if (TryResolveFallbackModel(stage.Name, out var fallbackModel))
        {
            var fallbackAvailability = await modelChecker.CheckAsync(fallbackModel, options.RepoRoot, cancellationToken);
            if (fallbackAvailability.IsAvailable)
            {
                return StageModelSelection.Fallback(configuredModel, fallbackModel, configuredAvailability.Error ?? "Configured model is unavailable.");
            }

            return StageModelSelection.Unavailable(configuredModel, configuredAvailability.Error ?? "Configured model is unavailable.", fallbackModel, fallbackAvailability.Error);
        }

        return StageModelSelection.Unavailable(configuredModel, configuredAvailability.Error ?? "Configured model is unavailable.");
    }

    private string ResolveConfiguredModel(string stageName)
    {
        return TryGetStageModel(options.StageModelOverrides, stageName, out var model) ? model : options.Model;
    }

    private bool TryResolveFallbackModel(string stageName, out string model)
    {
        return TryGetStageModel(options.StageModelFallbacks, stageName, out model);
    }

    private static bool TryGetStageModel(IReadOnlyDictionary<string, string>? models, string stageName, out string model)
    {
        model = string.Empty;
        if (models is null)
        {
            return false;
        }

        if (models.TryGetValue(stageName, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            model = exact;
            return true;
        }

        if (models.TryGetValue("*", out var wildcard) && !string.IsNullOrWhiteSpace(wildcard))
        {
            model = wildcard;
            return true;
        }

        return false;
    }
}

internal sealed record StageModelSelection(
    string ConfiguredModel,
    string SelectedModel,
    bool IsAvailable,
    string? FallbackModel = null,
    string? FallbackReason = null,
    string? Error = null)
{
    public static StageModelSelection Selected(string model) => new(model, model, true);

    public static StageModelSelection Fallback(string configuredModel, string fallbackModel, string reason) => new(configuredModel, fallbackModel, true, fallbackModel, reason);

    public static StageModelSelection Unavailable(string configuredModel, string error, string? fallbackModel = null, string? fallbackError = null)
    {
        var message = string.IsNullOrWhiteSpace(fallbackError) ? error : $"{error} Fallback '{fallbackModel}' is also unavailable: {fallbackError}";
        return new StageModelSelection(configuredModel, configuredModel, false, fallbackModel, error, message);
    }
}