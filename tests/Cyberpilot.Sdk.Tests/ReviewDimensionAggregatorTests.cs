using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class ReviewDimensionAggregatorTests
{
    [Fact]
    public void Aggregate_WhenAllDimensionsApprove_ReturnsApprovedReviewVerdict()
    {
        var results = new[]
        {
            DimensionResult("security", "approved"),
            DimensionResult("quality", "approved"),
        };

        var aggregate = ReviewDimensionAggregator.Aggregate(1, TimeSpan.FromSeconds(3), results);

        Assert.Equal("GO", aggregate.Status);
        Assert.Equal("approved", aggregate.Decision);
        Assert.Contains(aggregate.Artifacts!, artifact => artifact.Name == "review-verdict");
        Assert.Contains("security-reviewer", aggregate.Artifacts!.Single(artifact => artifact.Name == "review-verdict").Value);
        Assert.Equal(300, aggregate.InputTokens);
        Assert.Equal(30, aggregate.OutputTokens);
        Assert.Equal(3000, aggregate.Metrics!.DurationMs);
    }

    [Fact]
    public void Aggregate_WhenAnyDimensionRequestsChanges_ReturnsChangesRequested()
    {
        var results = new[]
        {
            DimensionResult("security", "approved"),
            DimensionResult("quality", "changes_requested", requiredActions: ["web/Controllers/HomeController.cs:12 fix the quality finding"]),
        };

        var aggregate = ReviewDimensionAggregator.Aggregate(1, TimeSpan.FromSeconds(3), results);

        Assert.Equal("GO", aggregate.Status);
        Assert.Equal("changes_requested", aggregate.Decision);
        Assert.Contains("Code Quality: web/Controllers/HomeController.cs:12 fix the quality finding", aggregate.RequiredActions!);
    }

    [Fact]
    public void Aggregate_WhenDimensionFails_ReturnsStopButKeepsOtherDimensionEvidence()
    {
        var failed = DimensionResult("security", "approved") with
        {
            Result = new StageResult("INVALID", "changes_requested", false, "bad json"),
        };
        var approved = DimensionResult("quality", "approved");

        var aggregate = ReviewDimensionAggregator.Aggregate(1, TimeSpan.FromSeconds(3), [failed, approved]);

        Assert.Equal("STOP", aggregate.Status);
        Assert.Equal("changes_requested", aggregate.Decision);
        Assert.Contains("security", aggregate.Error);
        Assert.Contains("valid structured result", aggregate.PolicyRationale);
        Assert.Contains(aggregate.Evidence!, evidence => evidence.Name == "review-dimension:quality");
        Assert.Contains("Security: produce a valid structured review result. bad json", aggregate.RequiredActions!);
    }

    private static ReviewDimensionResult DimensionResult(string id, string decision, IReadOnlyList<string>? requiredActions = null)
    {
        var definition = ReviewDimensionDefinitions.Defaults.Single(dimension => dimension.Id == id);
        var result = new StageResult(
            "GO",
            decision,
            true,
            null,
            InputTokens: id == "security" ? 100 : 200,
            OutputTokens: id == "security" ? 10 : 20,
            Metrics: new StageExecutionMetrics(InputTokens: id == "security" ? 100 : 200, OutputTokens: id == "security" ? 10 : 20, DurationMs: 1000),
            Artifacts: [new StageArtifact(definition.RequiredArtifact, $"{definition.DisplayName} clean.")],
            Evidence: [new StageEvidence("summary", $"{definition.DisplayName} reviewed.")],
            PolicyRationale: $"{definition.DisplayName} policy rationale.",
            RequiredActions: requiredActions);

        return new ReviewDimensionResult(definition, result, TimeSpan.FromSeconds(1), string.Empty);
    }
}
