using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class StageArtifactValidatorTests
{
    private static readonly PolicyProfile StandardPolicy = new("standard", PolicyStrictness.Standard);

    [Fact]
    public void Validate_LegacyResultWithoutArtifacts_ReturnsValid()
    {
        var validator = new DefaultStageArtifactValidator();
        var stage = Stage(requiredArtifacts: ["validation-summary"]);

        var result = validator.Validate(stage, StageResult.Empty, StandardPolicy);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithAllRequiredArtifacts_ReturnsValid()
    {
        var validator = new DefaultStageArtifactValidator();
        var stage = Stage(requiredArtifacts: ["validation-summary"]);
        var stageResult = StageResult.Empty with
        {
            Artifacts = [new StageArtifact("Validation-Summary", "green")],
        };

        var result = validator.Validate(stage, stageResult, StandardPolicy);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithPartialArtifacts_ReturnsMissingArtifactsAndActions()
    {
        var validator = new DefaultStageArtifactValidator();
        var stage = Stage(requiredArtifacts: ["pull-request", "validation-summary"]);
        var stageResult = StageResult.Empty with
        {
            Artifacts = [new StageArtifact("pull-request", "https://github.com/owner/repo/pull/1")],
        };

        var result = validator.Validate(stage, stageResult, StandardPolicy);

        Assert.False(result.IsValid);
        Assert.Equal(["validation-summary"], result.MissingArtifacts);
        Assert.Contains("validation-summary", result.Error);
        Assert.Equal(["Include artifact 'validation-summary' in the stage result."], result.RequiredActions);
    }

    [Fact]
    public void Validate_WithContractMismatch_ReturnsInvalid()
    {
        var validator = new DefaultStageArtifactValidator();
        var stage = Stage(contractVersion: "1.0");
        var stageResult = StageResult.Empty with { ContractVersion = "2.0" };

        var result = validator.Validate(stage, stageResult, StandardPolicy);

        Assert.False(result.IsValid);
        Assert.Contains("contract version '2.0'", result.Error);
        Assert.Equal(["Update the stage result to use contract version '1.0'."], result.RequiredActions);
    }

    private static PipelineStageDefinition Stage(string contractVersion = "1.0", IReadOnlyList<string>? requiredArtifacts = null)
        => new(
            new StageDefinition("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing"),
            new StageContract(contractVersion, requiredArtifacts ?? []),
            []);
}
