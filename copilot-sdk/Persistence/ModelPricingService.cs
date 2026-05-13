namespace Cyberpilot.Persistence;

/// <summary>
/// Maps Copilot model identifiers to per-1M-token USD rates for cost estimation.
/// Returns 0 for unknown models — this is a best-effort estimation tool, not billing truth.
/// </summary>
public static class ModelPricingService
{
    private sealed record ModelRates(decimal InputPer1M, decimal OutputPer1M);

    private static readonly Dictionary<string, ModelRates> PricingTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-sonnet-4.6"] = new(3.00m, 15.00m),
        ["claude-sonnet-4.5"] = new(3.00m, 15.00m),
        ["claude-haiku-4.5"]  = new(0.80m,  4.00m),
        ["claude-opus-4.7"]   = new(15.00m, 75.00m),
        ["claude-opus-4.6"]   = new(15.00m, 75.00m),
        ["claude-opus-4.5"]   = new(15.00m, 75.00m),
        ["gpt-4.1"]           = new(2.00m,  8.00m),
        ["gpt-5-mini"]        = new(0.15m,  0.60m),
    };

    /// <summary>
    /// Estimates the USD cost for a stage run based on model pricing and token counts.
    /// </summary>
    /// <param name="model">The model identifier.</param>
    /// <param name="inputTokens">The number of input tokens consumed.</param>
    /// <param name="outputTokens">The number of output tokens produced.</param>
    /// <returns>The estimated cost in USD, or <c>0</c> for unknown models or null token counts.</returns>
    public static decimal Estimate(string model, int? inputTokens, int? outputTokens)
    {
        if (!PricingTable.TryGetValue(model, out var rates))
        {
            return 0m;
        }

        var inputCost  = (inputTokens  ?? 0) * rates.InputPer1M  / 1_000_000m;
        var outputCost = (outputTokens ?? 0) * rates.OutputPer1M / 1_000_000m;
        return inputCost + outputCost;
    }
}
