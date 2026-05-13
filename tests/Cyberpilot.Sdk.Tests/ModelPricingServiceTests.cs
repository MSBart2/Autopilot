using Cyberpilot.Persistence;

namespace Cyberpilot.Sdk.Tests;

public sealed class ModelPricingServiceTests
{
    [Fact]
    public void Estimate_UnknownModel_ReturnsZero()
    {
        var cost = ModelPricingService.Estimate("gpt-99-ultra", 1_000_000, 1_000_000);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Estimate_EmptyModel_ReturnsZero()
    {
        var cost = ModelPricingService.Estimate("", 1_000_000, 1_000_000);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Estimate_BothTokensNull_ReturnsZero()
    {
        var cost = ModelPricingService.Estimate("claude-sonnet-4.6", null, null);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Estimate_InputTokensOnly_UsesInputRateOnly()
    {
        // claude-haiku-4.5: $0.80/1M input, $4.00/1M output
        var cost = ModelPricingService.Estimate("claude-haiku-4.5", 1_000_000, null);
        Assert.Equal(0.80m, cost);
    }

    [Fact]
    public void Estimate_OutputTokensOnly_UsesOutputRateOnly()
    {
        // claude-haiku-4.5: $0.80/1M input, $4.00/1M output
        var cost = ModelPricingService.Estimate("claude-haiku-4.5", null, 1_000_000);
        Assert.Equal(4.00m, cost);
    }

    [Theory]
    [InlineData("claude-sonnet-4.6", 1_000_000, 1_000_000, 18.00)]   // 3.00 + 15.00
    [InlineData("claude-sonnet-4.5", 1_000_000, 1_000_000, 18.00)]   // 3.00 + 15.00
    [InlineData("claude-haiku-4.5",  1_000_000, 1_000_000,  4.80)]   // 0.80 + 4.00
    [InlineData("claude-opus-4.7",   1_000_000, 1_000_000, 90.00)]   // 15.00 + 75.00
    [InlineData("claude-opus-4.6",   1_000_000, 1_000_000, 90.00)]   // 15.00 + 75.00
    [InlineData("claude-opus-4.5",   1_000_000, 1_000_000, 90.00)]   // 15.00 + 75.00
    [InlineData("gpt-4.1",           1_000_000, 1_000_000, 10.00)]   // 2.00 + 8.00
    [InlineData("gpt-5-mini",        1_000_000, 1_000_000,  0.75)]   // 0.15 + 0.60
    public void Estimate_KnownModel_ReturnsCorrectCost(string model, int input, int output, double expectedUsd)
    {
        var cost = ModelPricingService.Estimate(model, input, output);
        Assert.Equal((decimal)expectedUsd, cost);
    }

    [Fact]
    public void Estimate_ModelNameIsCaseInsensitive()
    {
        var lower = ModelPricingService.Estimate("claude-sonnet-4.6", 1_000_000, 0);
        var upper = ModelPricingService.Estimate("CLAUDE-SONNET-4.6", 1_000_000, 0);
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void Estimate_SmallTokenCounts_ReturnsAccurateDecimal()
    {
        // gpt-4.1: $2.00/1M input. 500 input tokens = $0.000001 × 500 = $0.001
        var cost = ModelPricingService.Estimate("gpt-4.1", 500, 0);
        Assert.Equal(0.001m, cost);
    }

    [Fact]
    public void Estimate_ZeroTokens_ReturnsZero()
    {
        var cost = ModelPricingService.Estimate("gpt-4.1", 0, 0);
        Assert.Equal(0m, cost);
    }
}
