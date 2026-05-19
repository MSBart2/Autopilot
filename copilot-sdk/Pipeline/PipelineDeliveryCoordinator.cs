using System.Text.Json;
using Cyberpilot.GitHub;

namespace Cyberpilot.Pipeline;

internal sealed record PipelineDeliveryOutcome(bool Succeeded, string Summary);

internal sealed class PipelineDeliveryCoordinator(
    PipelineExecutionContext context,
    IGitHubCli gitHubCli,
    ICyberpilotProgressSink progressSink,
    PipelineConsoleWriter console)
{
    public async Task<PipelineDeliveryOutcome> MergeApprovedPullRequestAsync(StageResult landingIntent, CancellationToken cancellationToken)
    {
        var prNumber = context.PullRequestNumber ?? TryReadPullRequestNumber(landingIntent);
        if (prNumber is null or <= 0)
        {
            return new PipelineDeliveryOutcome(false, "No pull request number is known for deterministic delivery.");
        }

        progressSink.OnDispatch(DispatchType.Gate, $"Delivery systems check started for PR #{prNumber}");
        PullRequestReadiness readiness;
        try
        {
            readiness = await LoadReadinessAsync(prNumber.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PipelineDeliveryOutcome(false, $"Could not load PR readiness for #{prNumber}: {ex.Message}");
        }

        if (!readiness.IsReady)
        {
            return new PipelineDeliveryOutcome(false, readiness.BlockingReason ?? $"PR #{prNumber} is not ready to merge.");
        }

        if (readiness.AlreadyMerged)
        {
            context.PrUrl = readiness.Url ?? context.PrUrl;
            context.KnownPullRequestNumber = prNumber;
            return new PipelineDeliveryOutcome(true, $"PR #{prNumber} was already merged.");
        }

        progressSink.OnDispatch(DispatchType.Gate, $"Delivery systems green for PR #{prNumber}: review approved, checks passing, mergeable");
        try
        {
            var subject = $"Delivery for issue #{context.IssueNumber}";
            await gitHubCli.RunAsync(
                ["pr", "merge", prNumber.Value.ToString(), "--squash", "--delete-branch", "--subject", subject, "--body", "Merged by Cyberpilot deterministic delivery."],
                allowFailure: false,
                cancellationToken);
            context.PrUrl = readiness.Url ?? context.PrUrl;
            context.KnownPullRequestNumber = prNumber;
            console.WriteSuccess($"PR #{prNumber} merged with squash strategy.");
            return new PipelineDeliveryOutcome(true, $"PR #{prNumber} merged with squash strategy; branch cleanup requested.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PipelineDeliveryOutcome(false, $"PR #{prNumber} merge failed: {ex.Message}");
        }
    }

    private async Task<PullRequestReadiness> LoadReadinessAsync(int prNumber, CancellationToken cancellationToken)
    {
        var raw = await gitHubCli.RunAsync(
            ["pr", "view", prNumber.ToString(), "--json", "number,title,state,url,headRefName,baseRefName,mergeable,reviewDecision,isDraft,statusCheckRollup"],
            allowFailure: false,
            cancellationToken);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var url = ReadString(root, "url");
        var state = ReadString(root, "state") ?? string.Empty;
        var reviewDecision = ReadString(root, "reviewDecision") ?? string.Empty;
        var mergeable = ReadString(root, "mergeable") ?? string.Empty;
        var isDraft = ReadBool(root, "isDraft") ?? false;
        var checks = ReadChecks(root).ToArray();

        if (state.Equals("MERGED", StringComparison.OrdinalIgnoreCase))
        {
            return new PullRequestReadiness(true, url, null, AlreadyMerged: true);
        }

        if (!state.Equals("OPEN", StringComparison.OrdinalIgnoreCase))
        {
            return new PullRequestReadiness(false, url, $"PR #{prNumber} is {state}, not open.");
        }

        if (isDraft)
        {
            return new PullRequestReadiness(false, url, $"PR #{prNumber} is still a draft.");
        }

        if (!reviewDecision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            return new PullRequestReadiness(false, url, $"PR #{prNumber} review decision is '{reviewDecision.DefaultIfBlank("unknown")}', not APPROVED.");
        }

        if (mergeable.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase))
        {
            return new PullRequestReadiness(false, url, $"PR #{prNumber} has merge conflicts.");
        }

        var blockedCheck = checks.FirstOrDefault(check => check.IsBlockingFailure || check.IsPending);
        if (blockedCheck is not null)
        {
            return new PullRequestReadiness(false, url, blockedCheck.IsPending
                ? $"PR #{prNumber} check '{blockedCheck.Name}' is still pending."
                : $"PR #{prNumber} check '{blockedCheck.Name}' concluded {blockedCheck.Conclusion}.");
        }

        return new PullRequestReadiness(true, url, null);
    }

    private static IEnumerable<CheckReadiness> ReadChecks(JsonElement root)
    {
        if (!root.TryGetProperty("statusCheckRollup", out var rollup) || rollup.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in rollup.EnumerateArray())
        {
            var name = ReadString(item, "name") ?? ReadString(item, "workflowName") ?? "status check";
            var status = ReadString(item, "status") ?? ReadString(item, "state") ?? string.Empty;
            var conclusion = ReadString(item, "conclusion") ?? ReadString(item, "state") ?? string.Empty;
            yield return new CheckReadiness(name, status, conclusion);
        }
    }

    private static int? TryReadPullRequestNumber(StageResult result)
    {
        var artifact = result.Artifacts?.FirstOrDefault(item => item.Name.Equals("pull-request", StringComparison.OrdinalIgnoreCase));
        var value = artifact?.Uri ?? artifact?.Value;
        if (string.IsNullOrWhiteSpace(value)) return null;
        var marker = "/pull/";
        var index = value.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return int.TryParse(value[(index + marker.Length)..].Trim('/'), out var parsed) ? parsed : null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private sealed record PullRequestReadiness(bool IsReady, string? Url, string? BlockingReason, bool AlreadyMerged = false);

    private sealed record CheckReadiness(string Name, string Status, string Conclusion)
    {
        public bool IsPending => string.IsNullOrWhiteSpace(Conclusion)
            || Status.Equals("PENDING", StringComparison.OrdinalIgnoreCase)
            || Status.Equals("IN_PROGRESS", StringComparison.OrdinalIgnoreCase)
            || Status.Equals("QUEUED", StringComparison.OrdinalIgnoreCase)
            || Status.Equals("REQUESTED", StringComparison.OrdinalIgnoreCase)
            || Status.Equals("WAITING", StringComparison.OrdinalIgnoreCase);

        public bool IsBlockingFailure => Conclusion.Equals("FAILURE", StringComparison.OrdinalIgnoreCase)
            || Conclusion.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)
            || Conclusion.Equals("TIMED_OUT", StringComparison.OrdinalIgnoreCase)
            || Conclusion.Equals("ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || Conclusion.Equals("ERROR", StringComparison.OrdinalIgnoreCase);
    }
}

file static class StringExtensions
{
    public static string DefaultIfBlank(this string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}