using Cyberpilot.Copilot;
using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed class StageModelResolver(CyberpilotOptions options, IModelAvailabilityChecker modelChecker)
{
    private static readonly IReadOnlyDictionary<string, ModelTier> DefaultStageTiers = new Dictionary<string, ModelTier>(StringComparer.OrdinalIgnoreCase)
    {
        ["triage"] = ModelTier.Small,
        ["plan"] = ModelTier.Medium,
        ["implement"] = ModelTier.Medium,
        ["review"] = ModelTier.Medium,
        ["docs"] = ModelTier.Small,
        ["deliver"] = ModelTier.Small,
    };

    private static readonly HashSet<string> EscalatableStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "plan",
        "implement",
        "review",
    };

    public async Task<StageModelSelection> ResolveAsync(StageDefinition stage, PipelineExecutionContext? context, CancellationToken cancellationToken)
    {
        var configuredModel = ResolveConfiguredModel(stage.Name, context, out var autoTiered);
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

    private string ResolveConfiguredModel(string stageName, PipelineExecutionContext? context, out bool autoTiered)
    {
        autoTiered = false;
        if (TryGetStageModel(options.StageModelOverrides, stageName, out var model))
        {
            return model;
        }

        if (TryResolveTieredModel(options.Model, ResolveStageTier(stageName, options.Model, context), out var tieredModel))
        {
            autoTiered = true;
            return tieredModel;
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

        var baseStageName = GetBaseStageName(stageName);
        if (!baseStageName.Equals(stageName, StringComparison.OrdinalIgnoreCase)
            && models.TryGetValue(baseStageName, out var baseStageModel)
            && !string.IsNullOrWhiteSpace(baseStageModel))
        {
            model = baseStageModel;
            return true;
        }

        if (models.TryGetValue("*", out var wildcard) && !string.IsNullOrWhiteSpace(wildcard))
        {
            model = wildcard;
            return true;
        }

        return false;
    }

    private static ModelTier? ResolveStageTier(string stageName, string baseModel, PipelineExecutionContext? context)
    {
        stageName = GetBaseStageName(stageName);
        if (!DefaultStageTiers.TryGetValue(stageName, out var defaultTier))
        {
            return null;
        }

        if (defaultTier >= ModelTier.Medium
            && TryGetModelTier(baseModel, out var baseTier)
            && baseTier > defaultTier)
        {
            defaultTier = baseTier;
        }

        if (EscalatableStages.Contains(stageName)
            && TryGetRecommendedTier(context, out var recommendedTier)
            && recommendedTier > defaultTier)
        {
            return recommendedTier;
        }

        return defaultTier;
    }

    private static bool TryGetRecommendedTier(PipelineExecutionContext? context, out ModelTier tier)
    {
        tier = ModelTier.Small;
        if (context is null)
        {
            return false;
        }

        var found = false;
        foreach (var summary in context.StageHistory)
        {
            if (!TryParseTier(summary.RecommendedModelTier, out var recommended))
            {
                continue;
            }

            if (!found || recommended > tier)
            {
                tier = recommended;
                found = true;
            }
        }

        return found;
    }

    private static string GetBaseStageName(string stageName)
    {
        var separator = stageName.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 ? stageName[..separator] : stageName;
    }

    private static bool TryResolveTieredModel(string baseModel, ModelTier? tier, out string model)
    {
        model = string.Empty;
        if (tier is null)
        {
            return false;
        }

        model = GetModelFamily(baseModel) switch
        {
            ModelFamily.Claude => tier.Value switch
            {
                ModelTier.Small => "claude-haiku-4.5",
                ModelTier.Medium => "claude-sonnet-4.6",
                ModelTier.Large => "claude-opus-4.6",
                _ => string.Empty,
            },
            ModelFamily.Gpt => tier.Value switch
            {
                ModelTier.Small => "gpt-5-mini",
                ModelTier.Medium => "gpt-5.4",
                ModelTier.Large => "gpt-5.5",
                _ => string.Empty,
            },
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(model);
    }

    private static bool TryParseTier(string? value, out ModelTier tier)
    {
        tier = ModelTier.Small;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "small" => SetTier(ModelTier.Small, out tier),
            "medium" => SetTier(ModelTier.Medium, out tier),
            "large" => SetTier(ModelTier.Large, out tier),
            _ => false,
        };
    }

    private static bool SetTier(ModelTier value, out ModelTier tier)
    {
        tier = value;
        return true;
    }

    private static bool TryGetModelTier(string model, out ModelTier tier)
    {
        tier = ModelTier.Medium;
        return model.ToLowerInvariant() switch
        {
            "claude-haiku-4.5" or "gpt-5-mini" => SetTier(ModelTier.Small, out tier),
            "claude-sonnet-4.6" or "gpt-5.4" => SetTier(ModelTier.Medium, out tier),
            "claude-opus-4.6" or "gpt-5.5" => SetTier(ModelTier.Large, out tier),
            _ => false,
        };
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

    private enum ModelTier
    {
        Small = 0,
        Medium = 1,
        Large = 2,
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
