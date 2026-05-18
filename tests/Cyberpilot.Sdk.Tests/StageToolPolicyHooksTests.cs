using Cyberpilot.Copilot;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;
using GitHub.Copilot.SDK;

namespace Cyberpilot.Sdk.Tests;

public sealed class StageToolPolicyHooksTests
{
    [Fact]
    public void EvaluatePreToolUse_ForHarnessReadTool_AllowsCall()
    {
        var hooks = CreateHooks("review");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "get_pr_details",
            ToolArgs = new { },
        });

        Assert.Equal("allow", output.PermissionDecision);
        Assert.Contains("Harness read-only", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForSelfReviewApprove_DeniesCall()
    {
        var hooks = CreateHooks("review");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "powershell",
            ToolArgs = new { command = "gh pr review 42 --approve --body \"LGTM\"" },
        });

        Assert.Equal("deny", output.PermissionDecision);
        Assert.False(output.SuppressOutput);
        Assert.Contains("GitHub does not allow", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForSelfReviewRequestChanges_DeniesCall()
    {
        var hooks = CreateHooks("review");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "powershell",
            ToolArgs = new { command = "gh pr review 42 --request-changes --body \"needs work\"" },
        });

        Assert.Equal("deny", output.PermissionDecision);
        Assert.Contains("GitHub does not allow", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForPrReviewComment_AllowsCall()
    {
        var hooks = CreateHooks("review");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "powershell",
            ToolArgs = new { command = "gh pr review 42 --comment --body \"looks good\"" },
        });

        Assert.Equal("allow", output.PermissionDecision);
    }

    [Fact]
    public void EvaluatePreToolUse_ForReadOnlyStageWriteCommand_DeniesCall()
    {
        var hooks = CreateHooks("review");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "run_in_terminal",
            ToolArgs = new { command = "git push origin feature" },
        });

        Assert.Equal("deny", output.PermissionDecision);
        Assert.True(output.SuppressOutput);
        Assert.Contains("read-only", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForWriteEnabledStageWriteCommand_AllowsCall()
    {
        var hooks = CreateHooks("implement");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "run_in_terminal",
            ToolArgs = new { command = "git commit -m test" },
        });

        Assert.Equal("allow", output.PermissionDecision);
    }

    [Fact]
    public void ShapePostToolUse_RedactsSecretsTruncatesOutputAndRecordsArtifact()
    {
        var context = CreateContext();
        var hooks = new StageToolPolicyHooks(Stage("review"), context);
        var secretOutput = $"token=abc123 {new string('x', 4100)}";

        var output = hooks.ShapePostToolUse(new PostToolUseHookInput
        {
            ToolName = "run_in_terminal",
            ToolResult = secretOutput,
        });

        var modified = Assert.IsType<string>(output.ModifiedResult);
        Assert.Contains("token=[REDACTED]", modified);
        Assert.Contains("...[truncated]", modified);
        Assert.Contains("redacted or truncated", output.AdditionalContext);
        var artifact = Assert.Single(context.GetToolArtifacts("review"));
        Assert.Equal("tool-hook-run_in_terminal", artifact.Name);
        Assert.DoesNotContain("abc123", artifact.Value);
    }

    private static StageToolPolicyHooks CreateHooks(string stageName)
    {
        return new StageToolPolicyHooks(Stage(stageName), CreateContext());
    }

    private static PipelineExecutionContext CreateContext()
    {
        return new PipelineExecutionContext(
            new CyberpilotOptions(
                42,
                Directory.GetCurrentDirectory(),
                "owner/repo",
                CyberpilotOptions.DefaultModel,
                false,
                false,
                false,
                false,
                CyberpilotOptions.DefaultStageTimeout,
                true,
                false,
                null,
                null,
                false),
            DefaultPipelineDefinitionProvider.Definition);
    }

    private static StageDefinition Stage(string name)
        => new(name.ToUpperInvariant(), name, $"{name}.agent.md", $"sdk/{name}");
}