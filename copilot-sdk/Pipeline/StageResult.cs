using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cyberpilot.Pipeline;

/// <summary>
/// Represents the structured result returned by one Cyberpilot pipeline stage.
/// </summary>
/// <param name="Status">The stage status.</param>
/// <param name="Decision">The review or stage decision.</param>
/// <param name="IsValid">Whether the stage output was valid.</param>
/// <param name="Error">The validation error, when invalid.</param>
public sealed record StageResult(string Status, string Decision, bool IsValid, string? Error)
{
    /// <summary>
    /// Gets an empty successful stage result.
    /// </summary>
    public static StageResult Empty { get; } = new("GO", "unknown", true, null);

    /// <summary>
    /// Parses the final fenced JSON result from a stage response.
    /// </summary>
    /// <param name="content">The raw stage response content.</param>
    /// <returns>The parsed stage result.</returns>
    public static StageResult Parse(string content)
    {
        var jsonMatches = Regex.Matches(content, "```json\\s*(?<json>\\{.*?\\})\\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (jsonMatches.Count == 0)
        {
            return Invalid("No fenced JSON result block found.");
        }

        var json = jsonMatches[^1].Groups["json"].Value;
        try
        {
            using var document = JsonDocument.Parse(json);
            var status = ReadString(document.RootElement, "status");
            if (string.IsNullOrWhiteSpace(status))
            {
                return Invalid("JSON result is missing required property 'status'.");
            }

            var normalizedStatus = status.ToUpperInvariant();
            if (normalizedStatus is not ("GO" or "STOP" or "DUPLICATE"))
            {
                return Invalid($"Unknown status '{status}'.");
            }

            var decision = ReadString(document.RootElement, "decision")?.ToLowerInvariant() ?? "unknown";
            if (decision is not ("unknown" or "approved" or "changes_requested" or "comment"))
            {
                return Invalid($"Unknown decision '{decision}'.");
            }

            return new StageResult(normalizedStatus, decision, true, null);
        }
        catch (JsonException ex)
        {
            return Invalid($"Malformed JSON result: {ex.Message}");
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) ? property.GetString() : null;
    }

    private static StageResult Invalid(string error)
    {
        return new StageResult("INVALID", "unknown", false, error);
    }
}
