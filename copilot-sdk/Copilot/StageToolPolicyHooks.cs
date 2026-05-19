using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cyberpilot.Pipeline;
using GitHub.Copilot.SDK;

namespace Cyberpilot.Copilot;

internal sealed partial class StageToolPolicyHooks(StageDefinition stage, PipelineExecutionContext context)
{
    private static readonly ToolOutputShapingPolicy DefaultOutputPolicy = new(
        MaxModelContextLength: 1200,
        MaxModelContextLines: 60,
        MaxDetailedArtifactLength: 16000);

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
        "render_stage_comment",
        "get_changed_file_content",
        "collect_validation_evidence",
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

        if (LooksLikeSelfReviewAttempt(input.ToolName, input.ToolArgs))
        {
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                PermissionDecisionReason = "GitHub does not allow a user to approve or request changes on a pull request they authored. Do not post a pr review verdict — summarize your findings in the issue comment instead.",
                SuppressOutput = false,
            };
        }

        if (LooksLikePowershellArrayArgs(input.ToolName, input.ToolArgs))
        {
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                PermissionDecisionReason = "The powershell tool accepts a single 'command' string argument. Pass the full command as one string, e.g. { \"command\": \"git status --porcelain\" }. Do not pass an array or multiple arguments.",
                SuppressOutput = false,
            };
        }

        if (!AllowsWrites(stage.Name) && LooksLikeWriteOperation(input.ToolName, input.ToolArgs))
        {
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                PermissionDecisionReason = $"Stage '{stage.Name}' may run investigative tools, but durable side effects are blocked. Return the intended comment, label, branch, or file change as a stage artifact instead; implementation, docs, and deliver stages may perform scoped writes.",
                SuppressOutput = false,
            };
        }

        return Allow($"Stage '{stage.Name}' tool policy allowed this tool call.");
    }

    public PostToolUseHookOutput ShapePostToolUse(PostToolUseHookInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var policy = DefaultOutputPolicy;
        var raw = Serialize(input.ToolResult);
        var redacted = SecretLikeValueRegex().Replace(raw, "$1$2[REDACTED]");
        var redactedSecrets = !string.Equals(raw, redacted, StringComparison.Ordinal);
        var truncatedForModel = redacted.Length > policy.MaxModelContextLength;
        var changed = redactedSecrets || truncatedForModel;
        var artifact = context.Options.CaptureToolOutputArtifacts && !string.IsNullOrWhiteSpace(redacted)
            ? PersistDetailedToolOutput(input.ToolName, raw.Length, redacted, redactedSecrets, policy)
            : null;

        if (!changed)
        {
            return new PostToolUseHookOutput();
        }

        var compactResult = BuildCompactToolResult(
            input.ToolName,
            raw.Length,
            redacted,
            redactedSecrets,
            truncatedForModel,
            artifact,
            policy);

        return new PostToolUseHookOutput
        {
            ModifiedResult = BuildModifiedToolResult(input.ToolResult, compactResult),
            AdditionalContext = artifact is not null
                ? "Tool output was redacted or compacted by Cyberpilot stage policy. Detailed redacted output is recorded as a run artifact; use the compact excerpt for model reasoning."
                : "Tool output was redacted or compacted by Cyberpilot stage policy. Enable tool output artifact capture to persist detailed redacted output for diagnostics.",
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

    private static bool LooksLikePowershellArrayArgs(string toolName, object? args)
    {
        // Detect when the model passes an array as the powershell command arg instead of a single string.
        // The powershell tool expects: { "command": "git status" }
        // A common mistake is: { "command": ["git", "status"] } or positional array args.
        if (!string.Equals(toolName, "powershell", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var serialized = Serialize(args);
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(serialized);
            var root = doc.RootElement;

            // Top-level array: model passed ["git", "status"] directly
            if (root.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            // Object with "command" key that is an array: { "command": ["git", "status"] }
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("command", out var commandProp) &&
                commandProp.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeSelfReviewAttempt(string toolName, object? args)
    {
        // GitHub rejects approve/request-changes reviews on PRs authored by the same user.
        // Detect it through any tool wrapper, including shell scripts.
        var serializedArgs = Serialize(args);
        return SelfReviewCommandRegex().IsMatch(serializedArgs);
    }

    private static bool LooksLikeWriteOperation(string toolName, object? args)
    {
        if (WriteToolNameRegex().IsMatch(toolName))
        {
            return true;
        }

        var serializedArgs = Serialize(args);
        var normalizedCommandText = CommandTokenSeparatorRegex().Replace(serializedArgs, " ");
        return DurableSideEffectCommandRegex().IsMatch(serializedArgs)
            || DurableSideEffectCommandRegex().IsMatch(normalizedCommandText)
            || FileMutationScriptRegex().IsMatch(serializedArgs);
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
            return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
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

    private StageArtifact PersistDetailedToolOutput(
        string toolName,
        int originalLength,
        string redactedOutput,
        bool redactedSecrets,
        ToolOutputShapingPolicy policy)
    {
        var artifactName = $"tool-output-{SanitizeName(toolName)}-detail";
        var uri = $"cyberpilot://tool-output/{Uri.EscapeDataString(stage.Name)}/{Uri.EscapeDataString(toolName)}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var artifactValue = BuildDetailedArtifactValue(toolName, originalLength, redactedOutput, redactedSecrets, policy);
        var artifact = new StageArtifact(artifactName, artifactValue, uri, "text/plain");
        context.RecordToolArtifact(stage.Name, artifact);
        return artifact;
    }

    private string BuildCompactToolResult(
        string toolName,
        int originalLength,
        string redactedOutput,
        bool redactedSecrets,
        bool truncatedForModel,
        StageArtifact? artifact,
        ToolOutputShapingPolicy policy)
    {
        var excerpt = Tail(redactedOutput, policy.MaxModelContextLines);
        if (excerpt.Length > policy.MaxModelContextLength)
        {
            excerpt = $"...[leading output omitted]{Environment.NewLine}{TailChars(excerpt, policy.MaxModelContextLength)}";
        }

        var artifactReference = artifact is null
            ? "not captured"
            : $"{artifact.Name} ({artifact.Uri})";

        return string.Join(Environment.NewLine, [
            "Cyberpilot compacted this tool output for model context.",
            $"Stage: {stage.Name}",
            $"Tool: {toolName}",
            $"Original characters: {originalLength}",
            $"Redacted secrets: {FormatBool(redactedSecrets)}",
            $"Truncated for model context: {FormatBool(truncatedForModel)}",
            $"Detailed redacted artifact: {artifactReference}",
            "",
            "Output excerpt (tail):",
            excerpt,
        ]);
    }

    private static object BuildModifiedToolResult(object? originalResult, string compactResult)
    {
        if (TryReadResultType(originalResult, out var resultType))
        {
            return new Dictionary<string, object?>
            {
                ["textResultForLlm"] = compactResult,
                ["resultType"] = resultType,
                ["sessionLog"] = compactResult,
                ["toolTelemetry"] = new Dictionary<string, object?>(),
            };
        }

        return compactResult;
    }

    private static string BuildDetailedArtifactValue(
        string toolName,
        int originalLength,
        string redactedOutput,
        bool redactedSecrets,
        ToolOutputShapingPolicy policy)
    {
        var artifactTruncated = redactedOutput.Length > policy.MaxDetailedArtifactLength;
        var output = artifactTruncated
            ? string.Concat(redactedOutput.AsSpan(0, policy.MaxDetailedArtifactLength), "...[detailed artifact truncated]")
            : redactedOutput;

        return string.Join(Environment.NewLine, [
            $"Tool: {toolName}",
            $"Original characters: {originalLength}",
            $"Redacted characters: {redactedOutput.Length}",
            $"Redacted secrets: {FormatBool(redactedSecrets)}",
            $"Artifact truncated: {FormatBool(artifactTruncated)}",
            "",
            output,
        ]);
    }

    private static string Tail(string value, int maxLines)
    {
        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
    }

    private static string TailChars(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[^maxLength..];
    }

    private static string FormatBool(bool value) => value ? "yes" : "no";

    private static bool TryReadResultType(object? value, out string resultType)
    {
        resultType = string.Empty;
        if (value is null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(Serialize(value));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("resultType", out var resultTypeProperty)
                && resultTypeProperty.ValueKind == JsonValueKind.String)
            {
                resultType = resultTypeProperty.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(resultType);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static string SanitizeName(string value)
    {
        var sanitized = ToolNameSafeCharactersRegex().Replace(value, "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    [GeneratedRegex("(api[_-]?key|token|secret|password|authorization|bearer)(\\s*[=:]\\s*)([^\\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretLikeValueRegex();

    [GeneratedRegex(@"gh\s+pr\s+review\b.*--(approve|request-changes)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelfReviewCommandRegex();

    [GeneratedRegex("(write|edit|create|delete|remove|rename|move|apply[_-]?patch|push|merge|commit)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WriteToolNameRegex();

    [GeneratedRegex(@"\bgh\s+(issue\s+(comment|edit|close|reopen|delete)|pr\s+(comment|review|merge|close|edit|create))\b|\bgh\s+api\b(?=.*\b(-X|--method)\s*(POST|PUT|PATCH|DELETE)\b)|\bgit\s+(push|commit|merge|rebase|reset|clean|tag|branch|checkout\s+-b|switch\s+-c)\b|\b(apply_patch|set-content|add-content|out-file|new-item|remove-item|move-item|rename-item|copy-item|mkdir|rm|del)\b|(?<!\d)>\s*(?!&)\S", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurableSideEffectCommandRegex();

    [GeneratedRegex(@"\bopen\s*\([^)]*,\s*[""'](?:w|a|x)\b|\.write_text\s*\(|\b(os\.(remove|unlink|rmdir|mkdir|makedirs|rename|replace)|shutil\.(move|copy|copyfile|copytree|rmtree))\s*\(|\bfs\.(writeFileSync|appendFileSync|rmSync|unlinkSync|mkdirSync|renameSync|cpSync|copyFileSync)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileMutationScriptRegex();

    [GeneratedRegex(@"[\[\]\{\}""',:]+", RegexOptions.CultureInvariant)]
    private static partial Regex CommandTokenSeparatorRegex();

    [GeneratedRegex("[^a-zA-Z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ToolNameSafeCharactersRegex();

    private sealed record ToolOutputShapingPolicy(
        int MaxModelContextLength,
        int MaxModelContextLines,
        int MaxDetailedArtifactLength);
}
