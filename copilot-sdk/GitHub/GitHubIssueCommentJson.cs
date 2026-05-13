using System.Text.Json;

namespace Cyberpilot.GitHub;

internal static class GitHubIssueCommentJson
{
    public static IReadOnlyList<GitHubIssueComment> ParseMany(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return ParseMany(document.RootElement);
    }

    public static IReadOnlyList<GitHubIssueComment> ParseMany(JsonElement root)
    {
        return root.EnumerateArray()
            .Select(Parse)
            .Where(comment => comment.Id > 0)
            .ToArray();
    }

    private static GitHubIssueComment Parse(JsonElement item)
    {
        return new GitHubIssueComment(
            item.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
            item.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
            item.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login) ? login.GetString() ?? string.Empty : string.Empty);
    }
}