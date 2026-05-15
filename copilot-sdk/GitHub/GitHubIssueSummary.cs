using System.Text.Json;

namespace Cyberpilot.GitHub;

/// <summary>
/// Summarizes a GitHub issue for dashboards and branch provisioning.
/// </summary>
public sealed record GitHubIssueSummary(
    int Number,
    string Title,
    string Url,
    IReadOnlyList<string> Labels,
    DateTimeOffset UpdatedAt,
    string State,
    bool HasActivePipelineRun,
    string Body = "",
    bool IsPullRequest = false,
    string? HeadBranch = null);

internal static class GitHubIssueSummaryJson
{
    public static IReadOnlyList<GitHubIssueSummary> ParseMany(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var issues = new List<GitHubIssueSummary>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            issues.Add(ParseIssue(item));
        }

        return issues;
    }

    public static GitHubIssueSummary ParseIssue(JsonElement item)
    {
        var labels = new List<string>();
        if (item.TryGetProperty("labels", out var labelArray) && labelArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labelArray.EnumerateArray())
            {
                if (label.TryGetProperty("name", out var name))
                {
                    labels.Add(name.GetString() ?? string.Empty);
                }
            }
        }

        var updatedAt = DateTimeOffset.MinValue;
        if (item.TryGetProperty("updated_at", out var updatedAtSnake) || item.TryGetProperty("updatedAt", out updatedAtSnake))
        {
            DateTimeOffset.TryParse(updatedAtSnake.GetString(), out updatedAt);
        }

        var number = item.TryGetProperty("number", out var numberValue) ? numberValue.GetInt32() : 0;
        var title = item.TryGetProperty("title", out var titleValue) ? titleValue.GetString() ?? string.Empty : string.Empty;
        var body = item.TryGetProperty("body", out var bodyValue) ? bodyValue.GetString() ?? string.Empty : string.Empty;
        var url = item.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() ?? string.Empty :
            item.TryGetProperty("url", out var urlValue) ? urlValue.GetString() ?? string.Empty : string.Empty;
        var state = item.TryGetProperty("state", out var stateValue) ? stateValue.GetString() ?? "OPEN" : "OPEN";

        return new GitHubIssueSummary(number, title, url, labels, updatedAt, state, HasActiveLabel(labels), body);
    }

    public static GitHubIssueSummary ParsePullRequest(JsonElement item)
    {
        var labels = new List<string>();
        if (item.TryGetProperty("labels", out var labelArray) && labelArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labelArray.EnumerateArray())
            {
                var labelName = label.ValueKind == JsonValueKind.String
                    ? label.GetString() ?? string.Empty
                    : label.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty;
                labels.Add(labelName);
            }
        }

        var updatedAt = DateTimeOffset.MinValue;
        if (item.TryGetProperty("updated_at", out var updatedAtSnake) || item.TryGetProperty("updatedAt", out updatedAtSnake))
        {
            DateTimeOffset.TryParse(updatedAtSnake.GetString(), out updatedAt);
        }

        var number = item.TryGetProperty("number", out var numberValue) ? numberValue.GetInt32() : 0;
        var title = item.TryGetProperty("title", out var titleValue) ? titleValue.GetString() ?? string.Empty : string.Empty;
        var url = item.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() ?? string.Empty :
            item.TryGetProperty("url", out var urlValue) ? urlValue.GetString() ?? string.Empty : string.Empty;
        var state = item.TryGetProperty("state", out var stateValue) ? stateValue.GetString() ?? "OPEN" : "OPEN";
        var headBranch = item.TryGetProperty("head", out var head) && head.TryGetProperty("ref", out var headRef)
            ? headRef.GetString()
            : item.TryGetProperty("headRefName", out var headRefName) ? headRefName.GetString() : null;

        return new GitHubIssueSummary(number, title, url, labels, updatedAt, state, false, string.Empty, IsPullRequest: true, HeadBranch: headBranch);
    }

    private static bool HasActiveLabel(IReadOnlyList<string> labels)
    {
        return labels.Any(label =>
            label.StartsWith("sdk/", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("local/", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("cloud/", StringComparison.OrdinalIgnoreCase));
    }
}
