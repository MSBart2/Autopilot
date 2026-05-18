using System.Text.Json;

namespace Cyberpilot.Pipeline;

/// <summary>
/// Represents the structured result returned by one Cyberpilot pipeline stage.
/// </summary>
/// <param name="Status">The stage status.</param>
/// <param name="Decision">The review or stage decision.</param>
/// <param name="IsValid">Whether the stage output was valid.</param>
/// <param name="Error">The validation error, when invalid.</param>
/// <param name="InputTokens">The number of input tokens consumed by this stage, when available.</param>
/// <param name="OutputTokens">The number of output tokens produced by this stage, when available.</param>
/// <param name="Metrics">The richer execution metrics captured while running the stage, when available.</param>
/// <param name="ContractVersion">The structured result contract version used by this stage result.</param>
/// <param name="Artifacts">The artifacts produced by the stage.</param>
/// <param name="Evidence">The evidence gathered or referenced by the stage.</param>
/// <param name="PolicyRationale">The policy rationale supplied by the stage.</param>
/// <param name="RequiredActions">The corrective actions required before the pipeline can continue.</param>
/// <param name="ConfiguredModel">The model configured for this stage before fallback.</param>
/// <param name="SelectedModel">The model selected for this stage after availability checks.</param>
/// <param name="FallbackModel">The fallback model used for this stage, when any.</param>
/// <param name="FallbackReason">The reason a fallback model was selected, when any.</param>
public sealed record StageResult(
    string Status,
    string Decision,
    bool IsValid,
    string? Error,
    int? InputTokens = null,
    int? OutputTokens = null,
    StageExecutionMetrics? Metrics = null,
    string? ContractVersion = PipelineDefinitionDefaults.ContractVersion,
    IReadOnlyList<StageArtifact>? Artifacts = null,
    IReadOnlyList<StageEvidence>? Evidence = null,
    string? PolicyRationale = null,
    IReadOnlyList<string>? RequiredActions = null,
    string? ConfiguredModel = null,
    string? SelectedModel = null,
    string? FallbackModel = null,
    string? FallbackReason = null)
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
        var json = ExtractLastJsonBlock(content);
        if (json is null)
        {
            return Invalid("No fenced JSON result block found.");
        }

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

            return new StageResult(
                normalizedStatus,
                decision,
                true,
                null,
                ContractVersion: ReadString(document.RootElement, "contractVersion")
                    ?? ReadString(document.RootElement, "contract_version")
                    ?? PipelineDefinitionDefaults.ContractVersion,
                Artifacts: ReadArtifacts(document.RootElement),
                Evidence: ReadEvidence(document.RootElement),
                PolicyRationale: ReadString(document.RootElement, "policyRationale")
                    ?? ReadString(document.RootElement, "policy_rationale"),
                RequiredActions: ReadStringArray(document.RootElement, "requiredActions")
                    ?? ReadStringArray(document.RootElement, "required_actions"));
        }
        catch (JsonException ex)
        {
            return Invalid($"Malformed JSON result: {ex.Message}");
        }
    }

    private static string? ExtractLastJsonBlock(string content)
    {
        const string FenceOpen = "```json";
        var searchStart = 0;
        string? lastJson = null;

        while (true)
        {
            var fenceStart = content.IndexOf(FenceOpen, searchStart, StringComparison.OrdinalIgnoreCase);
            if (fenceStart < 0) break;

            var contentStart = fenceStart + FenceOpen.Length;
            while (contentStart < content.Length && content[contentStart] is ' ' or '\t' or '\r' or '\n')
                contentStart++;

            if (contentStart < content.Length && content[contentStart] == '{')
            {
                var extracted = ExtractJsonObject(content, contentStart);
                if (extracted is not null)
                    lastJson = extracted;
            }

            searchStart = fenceStart + FenceOpen.Length;
        }

        return lastJson;
    }

    private static string? ExtractJsonObject(string content, int start)
    {
        if (start >= content.Length || content[start] != '{') return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < content.Length; i++)
        {
            var c = content[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return content[start..(i + 1)];
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) ? property.GetString() : null;
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        return values.Length == 0 ? null : values;
    }

    private static IReadOnlyList<StageArtifact>? ReadArtifacts(JsonElement element)
    {
        if (!element.TryGetProperty("artifacts", out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Object)
        {
            var artifacts = property.EnumerateObject()
                .Select(item => new StageArtifact(item.Name, ReadElementAsString(item.Value)))
                .ToArray();
            return artifacts.Length == 0 ? null : artifacts;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = property.EnumerateArray()
            .Select(ReadArtifact)
            .OfType<StageArtifact>()
            .ToArray();

        return values.Length == 0 ? null : values;
    }

    private static StageArtifact? ReadArtifact(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : new StageArtifact(value);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = ReadString(element, "name") ?? ReadString(element, "type");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new StageArtifact(
            name,
            ReadString(element, "value") ?? ReadString(element, "summary"),
            ReadString(element, "uri") ?? ReadString(element, "url"),
            ReadString(element, "mediaType") ?? ReadString(element, "media_type"));
    }

    private static IReadOnlyList<StageEvidence>? ReadEvidence(JsonElement element)
    {
        if (!element.TryGetProperty("evidence", out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = property.EnumerateArray()
            .Select(ReadEvidenceItem)
            .OfType<StageEvidence>()
            .ToArray();

        return values.Length == 0 ? null : values;
    }

    private static StageEvidence? ReadEvidenceItem(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : new StageEvidence(value, value);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = ReadString(element, "name") ?? ReadString(element, "type");
        var summary = ReadString(element, "summary") ?? ReadString(element, "value") ?? name;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return new StageEvidence(name, summary, ReadString(element, "uri") ?? ReadString(element, "url"));
    }

    private static string? ReadElementAsString(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
    }

    private static StageResult Invalid(string error)
    {
        return new StageResult("INVALID", "unknown", false, error);
    }
}

/// <summary>
/// Describes an artifact produced by a pipeline stage.
/// </summary>
/// <param name="Name">The artifact name or type.</param>
/// <param name="Value">The artifact value or summary, when available.</param>
/// <param name="Uri">A URI pointing to the artifact, when available.</param>
/// <param name="MediaType">The artifact media type, when available.</param>
public sealed record StageArtifact(string Name, string? Value = null, string? Uri = null, string? MediaType = null);

/// <summary>
/// Describes evidence captured or referenced by a pipeline stage.
/// </summary>
/// <param name="Name">The evidence name or type.</param>
/// <param name="Summary">A concise evidence summary.</param>
/// <param name="Uri">A URI pointing to evidence details, when available.</param>
public sealed record StageEvidence(string Name, string Summary, string? Uri = null);
