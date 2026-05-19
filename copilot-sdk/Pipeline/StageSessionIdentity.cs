namespace Cyberpilot.Pipeline;

internal static class StageSessionIdentity
{
    public static string Create(string? runId, string stageName, int attempt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);

        var stableRunId = string.IsNullOrWhiteSpace(runId) ? "adhoc" : runId;
        return $"cyberpilot-{Sanitize(stableRunId)}-{Sanitize(stageName)}-{attempt}";
    }

    private static string Sanitize(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
