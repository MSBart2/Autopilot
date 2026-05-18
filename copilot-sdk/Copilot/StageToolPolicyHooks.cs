using System.Text.Json;
using System.Text.RegularExpressions;
using Cyberpilot.Pipeline;
using GitHub.Copilot.SDK;

namespace Cyberpilot.Copilot;

internal sealed partial class StageToolPolicyHooks(StageDefinition stage, PipelineExecutionContext context)
{
    private const int MaxToolResultLength = 4000;
    private static readonly HashSet<string> WriteEnabledStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "implement",
        "docs",
        "deliver",
    };

    private static readonly HashSet<string> HarnessReadTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_pipeline_context",
        "get_pr_details",
        "get_pr_diff_summary",
    };

    public SessionHooks CreateHooks()
    {
        return new SessionHooks
        {
            OnPreToolUse = (input, _) => Task.FromResult<PreToolUseHookOutput?>(EvaluatePreToolUse(input)),
            OnPostToolUse = (input, _) => Task.FromResult<PostToolUseHookOutput?>(ShapePostToolUse(input)),
        };
    }

    public PreToolUseHookOutput EvaluatePreToolUse(PreToolUseHookInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (HarnessReadTools.Contains(input.ToolName))
        {
            return Allow("Harness read-only tool.");
        }

        if (!AllowsWrites(stage.Name) && LooksLikeWriteOperation(input.ToolName, input.ToolArgs))
        {
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                PermissionDecisionReason = $"Stage '{stage.Name}' is read-only for broad write operations. Move the write to an implementation, docs, or deliver stage.",
                SuppressOutput = true,
            };
        }

        return Allow($"Stage '{stage.Name}' tool policy allowed this tool call.");
    }

    public PostToolUseHookOutput ShapePostToolUse(PostToolUseHookInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var raw = Serialize(input.ToolResult);
        var redacted = SecretLikeValueRegex().Replace(raw, "$1$2[REDACTED]");
        var shaped = Truncate(redacted, MaxToolResultLength);
        var changed = !string.Equals(raw, shaped, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(shaped))
        {
            var artifactName = $"tool-hook-{SanitizeName(input.ToolName)}";
            var uri = $"cyberpilot://tool-output/{Uri.EscapeDataString(stage.Name)}/{Uri.EscapeDataString(input.ToolName)}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            context.RecordToolArtifact(stage.Name, new StageArtifact(artifactName, shaped, uri, "text/plain"));
        }

        return new PostToolUseHookOutput
        {
            ModifiedResult = changed ? shaped : null,
            AdditionalContext = changed ? "Tool output was redacted or truncated by Cyberpilot stage policy. Full shaped output is recorded as a run artifact." : null,
        };
    }

    private static PreToolUseHookOutput Allow(string reason)
    {
        return new PreToolUseHookOutput
        {
            PermissionDecision = "allow",
            PermissionDecisionReason = reason,
        };
    }

    private static bool AllowsWrites(string stageName)
    {
        return WriteEnabledStages.Contains(stageName);
    }

    private static bool LooksLikeWriteOperation(string toolName, object? args)
    {
        if (WriteToolNameRegex().IsMatch(toolName))
        {
            return true;
        }

        var serializedArgs = Serialize(args);
        return WriteCommandRegex().IsMatch(serializedArgs);
    }

    private static string Serialize(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        try
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (NotSupportedException)
        {
            return value.ToString() ?? string.Empty;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength), "...[truncated]");
    }

    private static string SanitizeName(string value)
    {
        var sanitized = ToolNameSafeCharactersRegex().Replace(value, "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    [GeneratedRegex("(api[_-]?key|token|secret|password|authorization|bearer)(\\s*[=:]\\s*)([^\\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretLikeValueRegex();

    [GeneratedRegex("(write|edit|create|delete|remove|rename|move|apply[_-]?patch|push|merge|commit)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WriteToolNameRegex();

    [GeneratedRegex("(git\\s+(push|commit|merge)|apply_patch|set-content|add-content|out-file|new-item|remove-item|mkdir|rm\\s|del\\s|>\\s*[^\\s])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WriteCommandRegex();

    [GeneratedRegex("[^a-zA-Z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ToolNameSafeCharactersRegex();
}