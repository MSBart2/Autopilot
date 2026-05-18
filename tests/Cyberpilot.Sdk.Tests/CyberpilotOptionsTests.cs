using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class CyberpilotOptionsTests
{
    private static readonly string RepoRoot = Directory.GetCurrentDirectory();

    [Fact]
    public void Parse_EmptyArgs_ReturnsShowHelp()
    {
        var options = CyberpilotOptions.Parse([]);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_HelpFlag_ReturnsShowHelp()
    {
        var options = CyberpilotOptions.Parse(["--help"]);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_ShortHelpFlag_ReturnsShowHelp()
    {
        var options = CyberpilotOptions.Parse(["-h"]);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_IssueNumberOnly_ParsesIssueNumber()
    {
        var options = CyberpilotOptions.Parse(["42", "--repo-root", RepoRoot]);
        Assert.Equal(42, options.IssueNumber);
    }

    [Fact]
    public void Parse_RunIssueNumber_ParsesIssueNumber()
    {
        var options = CyberpilotOptions.Parse(["run", "issue", "42", "--repo-root", RepoRoot]);
        Assert.Equal(42, options.IssueNumber);
    }

    [Fact]
    public void Parse_Model_SetsModel()
    {
        var options = CyberpilotOptions.Parse(["--check-model", "--model", "gpt-4", "--repo-root", RepoRoot]);
        Assert.Equal("gpt-4", options.Model);
    }

    [Fact]
    public void Parse_DefaultModel_UsesClaude()
    {
        var options = CyberpilotOptions.Parse(["--check-model", "--repo-root", RepoRoot]);
        Assert.Equal("claude-sonnet-4.6", options.Model);
    }

    [Fact]
    public void Parse_SkipDeliver_SetsFlag()
    {
        var options = CyberpilotOptions.Parse(["42", "--skip-deliver", "--repo-root", RepoRoot]);
        Assert.True(options.SkipDeliver);
    }

    [Fact]
    public void Parse_EnsureLabels_SetsFlag()
    {
        var options = CyberpilotOptions.Parse(["42", "--ensure-labels", "--repo-root", RepoRoot]);
        Assert.True(options.EnsureLabels);
    }

    [Fact]
    public void Parse_CheckLabelsOnly_SetsFlag()
    {
        var options = CyberpilotOptions.Parse(["--check-labels", "--repo-root", RepoRoot]);
        Assert.True(options.CheckLabelsOnly);
    }

    [Fact]
    public void Parse_CheckModelOnly_SetsFlag()
    {
        var options = CyberpilotOptions.Parse(["--check-model", "--repo-root", RepoRoot]);
        Assert.True(options.CheckModelOnly);
    }

    [Fact]
    public void Parse_ApproveAll_SetsFlag()
    {
        var options = CyberpilotOptions.Parse(["42", "--approve-all", "--repo-root", RepoRoot]);
        Assert.True(options.ApproveAll);
    }

    [Fact]
    public void Parse_AllowMissingDocs_SetsFlag()
    {
        var options = CyberpilotOptions.Parse(["42", "--allow-missing-docs", "--repo-root", RepoRoot]);
        Assert.True(options.AllowMissingDocs);
    }

    [Fact]
    public void Parse_StageTimeoutMinutes_SetsTimeout()
    {
        var options = CyberpilotOptions.Parse(["--check-model", "--stage-timeout-minutes", "5", "--repo-root", RepoRoot]);
        Assert.Equal(TimeSpan.FromMinutes(5), options.StageTimeout);
    }

    [Fact]
    public void Parse_StageModelOptions_SetOverrideAndFallbackMaps()
    {
        var options = CyberpilotOptions.Parse([
            "42",
            "--repo-root",
            RepoRoot,
            "--stage-model",
            "review=gpt-4.1",
            "--stage-fallback-model",
            "review=claude-haiku-4.5",
        ]);

        Assert.Equal("gpt-4.1", options.StageModelOverrides!["review"]);
        Assert.Equal("claude-haiku-4.5", options.StageModelFallbacks!["review"]);
    }

    [Fact]
    public void Parse_DefaultPipelineDefinition_UsesDefaultDefinitionMetadata()
    {
        var options = CyberpilotOptions.Parse(["--check-model", "--repo-root", RepoRoot]);

        Assert.Equal(DefaultPipelineDefinitionProvider.DefinitionName, options.PipelineDefinitionName);
        Assert.Equal(DefaultPipelineDefinitionProvider.DefinitionVersion, options.PipelineDefinitionVersion);
        Assert.Equal("standard", options.PolicyProfileName);
    }

    [Fact]
    public void Parse_PipelineDefinitionOptions_SetDefinitionMetadata()
    {
        var options = CyberpilotOptions.Parse(
            [
                "--check-model",
                "--pipeline-definition",
                "docs-only",
                "--pipeline-version",
                "2.0",
                "--policy-profile",
                "strict",
                "--repo-root",
                RepoRoot,
            ]);

        Assert.Equal("docs-only", options.PipelineDefinitionName);
        Assert.Equal("2.0", options.PipelineDefinitionVersion);
        Assert.Equal("strict", options.PolicyProfileName);
    }

    [Fact]
    public void Parse_PipelineDefinitionFileOption_SetsDefinitionFilePath()
    {
        var options = CyberpilotOptions.Parse(
            [
                "--check-model",
                "--pipeline-definition-file",
                "pipelines/custom.json",
                "--repo-root",
                RepoRoot,
            ]);

        Assert.Equal("pipelines/custom.json", options.PipelineDefinitionFilePath);
    }

    [Fact]
    public void Parse_EmptyPipelineDefinitionOption_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CyberpilotOptions.Parse(["--check-model", "--pipeline-definition", "", "--repo-root", RepoRoot]));
    }

    [Fact]
    public void Parse_InvalidStageTimeout_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CyberpilotOptions.Parse(["--check-model", "--stage-timeout-minutes", "-1", "--repo-root", RepoRoot]));
    }

    [Fact]
    public void Parse_MissingValueForOption_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CyberpilotOptions.Parse(["--check-model", "--model"]));
    }

    [Fact]
    public void Parse_NoIssueAndNotCheckOnly_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CyberpilotOptions.Parse(["--approve-all", "--repo-root", RepoRoot]));
    }

    [Fact]
    public void Parse_DbOption_SetsConnectionString()
    {
        var options = CyberpilotOptions.Parse(["--check-model", "--db", "Data Source=test.db", "--repo-root", RepoRoot]);
        Assert.Equal("Data Source=test.db", options.DatabaseConnectionString);
    }

    [Fact]
    public void Parse_ConfigOption_SetsConfigPath()
    {
        var options = CyberpilotOptions.Parse(["--check-model", "--config", "web/appsettings.json", "--repo-root", RepoRoot]);
        Assert.Equal("web/appsettings.json", options.ConfigPath);
    }

    [Fact]
    public void Parse_RepoOption_SetsRepository()
    {
        var options = CyberpilotOptions.Parse(["--check-model", "--repo", "owner/name", "--repo-root", RepoRoot]);
        Assert.Equal("owner/name", options.Repository);
    }

    [Fact]
    public void Parse_RepoRootOption_SetsExplicitRoot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cyberpilot-test-{Guid.NewGuid():N}");
        var agentsDir = Path.Combine(tempDir, ".github", "agents");
        Directory.CreateDirectory(agentsDir);
        try
        {
            var options = CyberpilotOptions.Parse(["--check-model", "--repo-root", tempDir]);
            Assert.Equal(Path.GetFullPath(tempDir), options.RepoRoot);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void HelpText_ContainsUsageInfo()
    {
        Assert.Contains("Usage:", CyberpilotOptions.HelpText);
        Assert.Contains("--model", CyberpilotOptions.HelpText);
        Assert.Contains("--config", CyberpilotOptions.HelpText);
        Assert.Contains("--pipeline-definition", CyberpilotOptions.HelpText);
        Assert.Contains("--pipeline-definition-file", CyberpilotOptions.HelpText);
        Assert.Contains("docs-only", CyberpilotOptions.HelpText);
        Assert.Contains("--policy-profile", CyberpilotOptions.HelpText);
        Assert.Contains("security-critical", CyberpilotOptions.HelpText);
    }

    [Fact]
    public void DefaultModel_IsClaude()
    {
        Assert.Equal("claude-sonnet-4.6", CyberpilotOptions.DefaultModel);
    }

    [Fact]
    public void DefaultStageTimeout_IsTenMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), CyberpilotOptions.DefaultStageTimeout);
    }
}
