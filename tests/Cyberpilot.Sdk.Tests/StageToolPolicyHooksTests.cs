using Cyberpilot.Copilot;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;
using GitHub.Copilot.SDK;
using System.Text.Json;

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
    public void EvaluatePreToolUse_ForReadOnlyStagePrReviewComment_DeniesCall()
    {
        var hooks = CreateHooks("review");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "powershell",
            ToolArgs = new { command = "gh pr review 42 --comment --body \"looks good\"" },
        });

        Assert.Equal("deny", output.PermissionDecision);
        Assert.Contains("durable side effects", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForPowershellArrayCommand_DeniesCall()
    {
        var hooks = CreateHooks("plan");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "powershell",
            ToolArgs = new { command = new[] { "git", "status", "--porcelain" } },
        });

        Assert.Equal("deny", output.PermissionDecision);
        Assert.False(output.SuppressOutput);
        Assert.Contains("single 'command' string", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForPowershellStringCommand_AllowsCall()
    {
        var hooks = CreateHooks("plan");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "powershell",
            ToolArgs = new { command = "git status --porcelain" },
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
        Assert.False(output.SuppressOutput);
        Assert.Contains("durable side effects", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForReadOnlyStagePythonReadScript_AllowsCall()
    {
        var hooks = CreateHooks("plan");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "python",
            ToolArgs = new { code = "from pathlib import Path\nprint(Path('README.md').read_text()[:80])" },
        });

        Assert.Equal("allow", output.PermissionDecision);
    }

    [Fact]
    public void EvaluatePreToolUse_ForReadOnlyStagePythonIssueComment_DeniesCall()
    {
        var hooks = CreateHooks("plan");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "python",
            ToolArgs = new { code = "import subprocess\nsubprocess.run(['gh', 'issue', 'comment', '34', '--body', 'plan'])" },
        });

        Assert.Equal("deny", output.PermissionDecision);
        Assert.False(output.SuppressOutput);
        Assert.Contains("stage artifact", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForReadOnlyStagePythonFileWrite_DeniesCall()
    {
        var hooks = CreateHooks("triage");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "python",
            ToolArgs = new { code = "open('notes.txt', 'w').write('nope')" },
        });

        Assert.Equal("deny", output.PermissionDecision);
        Assert.Contains("durable side effects", output.PermissionDecisionReason);
    }

    [Fact]
    public void EvaluatePreToolUse_ForReadOnlyStageNodeFileWrite_DeniesCall()
    {
        var hooks = CreateHooks("plan");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "node",
            ToolArgs = new { code = "const fs = require('fs'); fs.writeFileSync('plan.txt', 'nope');" },
        });

        Assert.Equal("deny", output.PermissionDecision);
    }

    [Fact]
    public void EvaluatePreToolUse_ForReadOnlyStageStderrRedirect_AllowsCall()
    {
        var hooks = CreateHooks("triage");

        var output = hooks.EvaluatePreToolUse(new PreToolUseHookInput
        {
            ToolName = "powershell",
            ToolArgs = new { command = "gh issue list --repo owner/repo 2>&1" },
        });

        Assert.Equal("allow", output.PermissionDecision);
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
    public void ShapePostToolUse_RedactsSecretsTruncatesOutputWithoutArtifactByDefault()
    {
        var context = CreateContext();
        var hooks = new StageToolPolicyHooks(Stage("review"), context);
        var secretOutput = $"token=abc123 {new string('x', 4100)}";

        var output = hooks.ShapePostToolUse(new PostToolUseHookInput
        {
            ToolName = "run_in_terminal",
            ToolResult = secretOutput,
        });

        var modified = ExtractModelText(output.ModifiedResult);
        Assert.Contains("Cyberpilot compacted this tool output", modified);
        Assert.Contains("Redacted secrets: yes", modified);
        Assert.Contains("Truncated for model context: yes", modified);
        Assert.Contains("Detailed redacted artifact: not captured", modified);
        Assert.DoesNotContain("abc123", modified);
        Assert.True(modified.Length < secretOutput.Length);
        Assert.Contains("Enable tool output artifact capture", output.AdditionalContext);
        Assert.Empty(context.GetToolArtifacts("review"));
    }

    [Fact]
    public void ShapePostToolUse_WhenCaptureEnabled_RecordsArtifact()
    {
        var context = CreateContext(captureToolOutputArtifacts: true);
        var hooks = new StageToolPolicyHooks(Stage("review"), context);
        var secretOutput = $"token=abc123 {new string('x', 4100)}";

        var output = hooks.ShapePostToolUse(new PostToolUseHookInput
        {
            ToolName = "run_in_terminal",
            ToolResult = secretOutput,
        });

        var modified = ExtractModelText(output.ModifiedResult);
        Assert.Contains("Detailed redacted artifact: tool-output-run_in_terminal-detail", modified);
        Assert.Contains("Detailed redacted output is recorded", output.AdditionalContext);
        var artifact = Assert.Single(context.GetToolArtifacts("review"));
        Assert.Equal("tool-output-run_in_terminal-detail", artifact.Name);
        Assert.Contains("token=[REDACTED]", artifact.Value);
        Assert.Contains("Artifact truncated: no", artifact.Value);
        Assert.DoesNotContain("abc123", artifact.Value);
    }

    [Fact]
    public void ShapePostToolUse_PreservesSdkSuccessEnvelopeWhenCompacting()
    {
        var hooks = new StageToolPolicyHooks(Stage("review"), CreateContext());

        var output = hooks.ShapePostToolUse(new PostToolUseHookInput
        {
            ToolName = "powershell",
            ToolResult = new
            {
                textResultForLlm = new string('x', 2_000),
                resultType = "success",
                sessionLog = "full log",
                toolTelemetry = new { },
            },
        });

        var serialized = JsonSerializer.Serialize(output.ModifiedResult);
        using var document = JsonDocument.Parse(serialized);
        Assert.Equal("success", document.RootElement.GetProperty("resultType").GetString());
        Assert.Contains("Cyberpilot compacted this tool output", document.RootElement.GetProperty("textResultForLlm").GetString());
    }

    private static StageToolPolicyHooks CreateHooks(string stageName)
    {
        return new StageToolPolicyHooks(Stage(stageName), CreateContext());
    }

    private static PipelineExecutionContext CreateContext(bool captureToolOutputArtifacts = false)
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
                false,
                RuntimePreferences: new CyberpilotRuntimePreferences(CaptureToolOutputArtifacts: captureToolOutputArtifacts)),
            DefaultPipelineDefinitionProvider.Definition);
    }

    private static StageDefinition Stage(string name)
        => new(name.ToUpperInvariant(), name, $"{name}.agent.md", $"sdk/{name}");

    private static string ExtractModelText(object? value)
    {
        var serialized = JsonSerializer.Serialize(value);
        using var document = JsonDocument.Parse(serialized);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("textResultForLlm", out var text)
            ? text.GetString() ?? string.Empty
            : document.RootElement.GetString() ?? string.Empty;
    }
}
