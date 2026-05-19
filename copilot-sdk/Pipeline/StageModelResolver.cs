using Cyberpilot.Copilot;
using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed class StageModelResolver(CyberpilotOptions options, IModelAvailabilityChecker modelChecker)
{
    private static readonly HashSet<string> CheapStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "triage",
        "plan",
        "docs",
        "deliver",
    };

    public async Task<StageModelSelection> ResolveAsync(StageDefinition stage, CancellationToken cancellationToken)
    {
        var configuredModel = ResolveConfiguredModel(stage.Name, out var autoTiered);
        var configuredAvailability = await modelChecker.CheckAsync(configuredModel, options.RepoRoot, cancellationToken);
        if (configuredAvailability.IsAvailable)
        {
            return StageModelSelection.Selected(configuredModel);
        }

        if (TryResolveFallbackModel(stage.Name, configuredModel, autoTiered, out var fallbackModel))
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

    private string ResolveConfiguredModel(string stageName, out bool autoTiered)
    {
        autoTiered = false;
        if (TryGetStageModel(options.StageModelOverrides, stageName, out var model))
        {
            return model;
        }

        if (TryGetFamilyCheapModel(options.Model, stageName, out var cheapModel))
        {
            autoTiered = true;
            return cheapModel;
        }

        return options.Model;
    }

    private bool TryResolveFallbackModel(string stageName, string configuredModel, bool autoTiered, out string model)
    {
        if (TryGetStageModel(options.StageModelFallbacks, stageName, out model))
        {
            return true;
        }

        if (autoTiered && !configuredModel.Equals(options.Model, StringComparison.OrdinalIgnoreCase))
        {
            model = options.Model;
            return true;
        }

        model = string.Empty;
        return false;
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

    private static bool TryGetFamilyCheapModel(string baseModel, string stageName, out string model)
    {
        model = string.Empty;
        if (!CheapStages.Contains(stageName))
        {
            return false;
        }

        model = GetModelFamily(baseModel) switch
        {
            ModelFamily.Claude when !baseModel.Equals("claude-haiku-4.5", StringComparison.OrdinalIgnoreCase) => "claude-haiku-4.5",
            ModelFamily.Gpt when !baseModel.Equals("gpt-5-mini", StringComparison.OrdinalIgnoreCase) => "gpt-5-mini",
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(model);
    }

    private static ModelFamily GetModelFamily(string model)
    {
        if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
        {
            return ModelFamily.Claude;
        }

        if (model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
        {
            return ModelFamily.Gpt;
        }

        return ModelFamily.Unknown;
    }

    private enum ModelFamily
    {
        Unknown,
        Claude,
        Gpt,
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
