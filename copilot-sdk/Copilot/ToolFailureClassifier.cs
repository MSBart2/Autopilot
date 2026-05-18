using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cyberpilot.Copilot;

internal static partial class ToolFailureClassifier
{
    private static readonly HashSet<string> WriteEnabledStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "implement",
        "docs",
        "deliver",
    };

    public static (string Code, string Message) Classify(string stageName, string? toolName, string? toolArgs, string? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(errorMessage))
        {
            return (
                string.IsNullOrWhiteSpace(errorCode) ? "tool_execution_failed" : errorCode,
                string.IsNullOrWhiteSpace(errorMessage) ? "Tool execution failed; the SDK did not provide a message." : errorMessage);
        }

        var normalizedToolName = string.IsNullOrWhiteSpace(toolName) ? "unknown" : toolName;
        var command = ExtractCommand(toolArgs);
        var searchable = $"{normalizedToolName} {toolArgs} {command}".ToLowerInvariant();

        if (!WriteEnabledStages.Contains(stageName) && DurableSideEffectRegex().IsMatch(searchable))
        {
            return ("policy_denied", $"Stage '{stageName}' blocked a durable side-effect tool call. Return the intended write as a stage artifact instead.");
        }

        if (string.Equals(normalizedToolName, "powershell", StringComparison.OrdinalIgnoreCase) && LooksLikeUnixUtilityInPowerShell(command))
        {
            return ("command_style_mismatch", "PowerShell command appears to use Unix-only utilities such as tail/head/grep/cat. Use PowerShell-native commands like Select-Object -Last, Select-String, and Get-Content.");
        }

        if (string.Equals(normalizedToolName, "view", StringComparison.OrdinalIgnoreCase))
        {
            return ("file_read_failed", "File read failed. Verify that the path exists, uses the target OS path separator, and is within the accessible workspace.");
        }

        if (string.Equals(normalizedToolName, "grep", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "rg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "glob", StringComparison.OrdinalIgnoreCase))
        {
            return ("search_failed", "Search tool failed. Verify the search path, glob pattern, and regular expression syntax.");
        }

        return ("tool_execution_failed", $"Tool '{normalizedToolName}' reported failure, but the SDK did not provide error details. Inspect ToolArgs and any persisted tool output for the underlying cause.");
    }

    private static string ExtractCommand(string? toolArgs)
    {
        if (string.IsNullOrWhiteSpace(toolArgs))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(toolArgs);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("command", out var commandProperty))
            {
                return commandProperty.ValueKind switch
                {
                    JsonValueKind.String => commandProperty.GetString() ?? string.Empty,
                    JsonValueKind.Array => string.Join(" ", commandProperty.EnumerateArray().Select(item => item.ToString())),
                    _ => commandProperty.ToString(),
                };
            }
        }
        catch (JsonException)
        {
            return toolArgs;
        }

        return toolArgs;
    }

    private static bool LooksLikeUnixUtilityInPowerShell(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        return UnixUtilityRegex().IsMatch(command);
    }

    [GeneratedRegex(@"(^|[|&;]\s*)(tail|head|grep|cat)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnixUtilityRegex();

    [GeneratedRegex(@"\b(gh\s+(issue\s+(comment|edit|close|reopen|delete)|pr\s+(comment|review|merge|close|edit|create)|api\s+.*\s-X\s+(POST|PUT|PATCH|DELETE))|git\s+(push|commit|merge|rebase|reset|clean|tag)|apply_patch)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DurableSideEffectRegex();
}
