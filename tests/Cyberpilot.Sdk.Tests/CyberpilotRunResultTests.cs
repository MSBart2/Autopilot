namespace Cyberpilot.Sdk.Tests;

public sealed class CyberpilotRunResultTests
{
    [Fact]
    public void FromExitCode_SetsAllPropertiesCorrectly()
    {
        var result = CyberpilotRunResult.FromExitCode(0, "deliver", "completed", branchName: "sdk/issue-1-fix");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("deliver", result.FinalStage);
        Assert.Equal("completed", result.Status);
        Assert.Equal("sdk/issue-1-fix", result.BranchName);
    }

    [Fact]
    public void FromExitCode_DefaultsOptionalPropertiesToNull()
    {
        var result = CyberpilotRunResult.FromExitCode(0, "triage", "ok");

        Assert.Null(result.BranchName);
        Assert.Null(result.PrUrl);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FromExitCode_SetsError()
    {
        var result = CyberpilotRunResult.FromExitCode(1, "plan", "failed", error: "timeout");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("timeout", result.Error);
    }

    [Fact]
    public void FromExitCode_StageResultsDefaultsToEmpty()
    {
        var result = CyberpilotRunResult.FromExitCode(0, "deliver", "completed");

        Assert.Empty(result.StageResults);
    }

    [Fact]
    public void FromExitCode_SetsPrUrlAndStageResults()
    {
        var stageResults = new[] { new Cyberpilot.Pipeline.StageResult("GO", "approved", true, null) };
        var result = CyberpilotRunResult.FromExitCode(0, "deliver", "completed", branchName: "main", prUrl: "https://github.com/example/repo/pull/1", error: "e", stageResults: stageResults);

        Assert.Equal("https://github.com/example/repo/pull/1", result.PrUrl);
        Assert.Equal(stageResults, result.StageResults);
    }
}
