namespace Cyberpilot.GitHub;

/// <summary>
/// Normalizes GitHub repository inputs to owner/name form.
/// </summary>
public static class GitHubRepositoryParser
{
    /// <summary>
    /// Attempts to normalize a repository URL, SSH URL, or owner/name value.
    /// </summary>
    /// <param name="input">The repository input.</param>
    /// <param name="repository">The normalized owner/name value when parsing succeeds.</param>
    /// <returns>True when the repository input can be normalized.</returns>
    public static bool TryNormalize(string? input, out string repository)
    {
        repository = string.Empty;
        var value = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["git@github.com:".Length..];
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            value = uri.AbsolutePath.Trim('/');
        }

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IsSafeSegment(parts[0]) || !IsSafeSegment(parts[1]))
        {
            return false;
        }

        repository = $"{parts[0]}/{parts[1]}";
        return true;
    }

    private static bool IsSafeSegment(string value)
    {
        return value.Length > 0 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
    }
}