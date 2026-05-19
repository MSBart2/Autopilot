using System.Text;

namespace Cyberpilot.Pipeline;

internal static class ReviewDimensionAggregator
{
    public static StageResult Aggregate(int cycle, TimeSpan wallClockDuration, IReadOnlyList<ReviewDimensionResult> dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);

        var infrastructureFailures = dimensions
            .Where(dimension => !dimension.Result.IsValid || !StageStatus.IsGo(dimension.Result))
            .ToArray();
        var requestedChanges = dimensions
            .Where(dimension => StageDecision.RequestsChanges(dimension.Result))
            .ToArray();

        var status = infrastructureFailures.Length > 0 ? "STOP" : "GO";
        var decision = infrastructureFailures.Length > 0 || requestedChanges.Length > 0
            ? "changes_requested"
            : "approved";

        var requiredActions = dimensions
            .SelectMany(ToRequiredActions)
            .ToArray();

        var artifacts = new List<StageArtifact>
        {
            new("review-verdict", BuildVerdictMarkdown(cycle, wallClockDuration, dimensions, status, decision), MediaType: "text/markdown"),
        };
        artifacts.AddRange(dimensions.SelectMany(ToDimensionArtifacts));

        var evidence = dimensions.SelectMany(ToEvidence).ToList();
        evidence.Add(new StageEvidence(
            "parallel-review-summary",
            $"{dimensions.Count} review dimensions completed in {PipelineConsoleWriter.FormatDuration(wallClockDuration)}; final decision: {decision}."));

        return new StageResult(
            status,
            decision,
            true,
            infrastructureFailures.Length == 0
                ? null
                : $"Review dimension infrastructure failed: {string.Join(", ", infrastructureFailures.Select(d => d.Dimension.Id))}.",
            InputTokens: Sum(dimensions.Select(d => d.Result.InputTokens)),
            OutputTokens: Sum(dimensions.Select(d => d.Result.OutputTokens)),
            Metrics: AggregateMetrics(dimensions, wallClockDuration),
            Artifacts: artifacts,
            Evidence: evidence,
            PolicyRationale: BuildPolicyRationale(infrastructureFailures, requestedChanges),
            RequiredActions: requiredActions.Length == 0 ? null : requiredActions,
            RecommendedModelTier: ResolveRecommendedTier(dimensions));
    }

    private static string BuildVerdictMarkdown(int cycle, TimeSpan wallClockDuration, IReadOnlyList<ReviewDimensionResult> dimensions, string status, string decision)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Parallel Review Verdict");
        builder.AppendLine();
        builder.AppendLine($"Review cycle {cycle} completed {dimensions.Count} read-only dimensions in {PipelineConsoleWriter.FormatDuration(wallClockDuration)}.");
        builder.AppendLine();
        builder.AppendLine("| Dimension | Participant | Status | Decision | Duration | Tokens | Summary |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var dimension in dimensions)
        {
            var tokens = FormatTokens(dimension.Result.InputTokens, dimension.Result.OutputTokens);
            builder.AppendLine($"| {dimension.Dimension.DisplayName} | `{dimension.Dimension.Participant}` | {dimension.Result.Status} | {dimension.Result.Decision} | {PipelineConsoleWriter.FormatDuration(dimension.WallClockDuration)} | {tokens} | {EscapeTable(Summarize(dimension.Result))} |");
        }

        builder.AppendLine();
        builder.AppendLine($"**Final status:** {status}");
        builder.AppendLine($"**Final decision:** {decision}");
        return builder.ToString().Trim();
    }

    private static string BuildPolicyRationale(IReadOnlyList<ReviewDimensionResult> infrastructureFailures, IReadOnlyList<ReviewDimensionResult> requestedChanges)
    {
        if (infrastructureFailures.Count > 0)
        {
            return "Parallel review fails closed when any read-only review dimension cannot produce a valid GO result, while preserving all completed dimension output for diagnosis.";
        }

        if (requestedChanges.Count > 0)
        {
            return "Parallel review requests changes because at least one specialist dimension reported blocking findings under the active review policy.";
        }

        return "Parallel review approved because every specialist dimension completed successfully without requesting changes.";
    }

    private static IEnumerable<string> ToRequiredActions(ReviewDimensionResult dimension)
    {
        if (!dimension.Result.IsValid)
        {
            yield return $"{dimension.Dimension.DisplayName}: produce a valid structured review result. {dimension.Result.Error}";
        }
        else if (!StageStatus.IsGo(dimension.Result))
        {
            yield return $"{dimension.Dimension.DisplayName}: resolve review dimension status {dimension.Result.Status}. {dimension.Result.Error}";
        }

        foreach (var action in dimension.Result.RequiredActions ?? [])
        {
            yield return $"{dimension.Dimension.DisplayName}: {action}";
        }
    }

    private static IEnumerable<StageArtifact> ToDimensionArtifacts(ReviewDimensionResult dimension)
    {
        var artifact = dimension.Result.Artifacts?
            .FirstOrDefault(item => item.Name.Equals(dimension.Dimension.RequiredArtifact, StringComparison.OrdinalIgnoreCase));
        if (artifact is not null)
        {
            yield return artifact;
            yield break;
        }

        yield return new StageArtifact(
            dimension.Dimension.RequiredArtifact,
            Summarize(dimension.Result),
            MediaType: "text/markdown");
    }

    private static IEnumerable<StageEvidence> ToEvidence(ReviewDimensionResult dimension)
    {
        yield return new StageEvidence(
            $"review-dimension:{dimension.Dimension.Id}",
            $"{dimension.Dimension.DisplayName} by {dimension.Dimension.Participant}: {dimension.Result.Status}/{dimension.Result.Decision}; duration {PipelineConsoleWriter.FormatDuration(dimension.WallClockDuration)}; tokens {FormatTokens(dimension.Result.InputTokens, dimension.Result.OutputTokens)}.");

        foreach (var evidence in dimension.Result.Evidence ?? [])
        {
            yield return new StageEvidence(
                $"{dimension.Dimension.Id}:{evidence.Name}",
                evidence.Summary,
                evidence.Uri);
        }
    }

    private static StageExecutionMetrics AggregateMetrics(IReadOnlyList<ReviewDimensionResult> dimensions, TimeSpan wallClockDuration)
        => new(
            "parallel-review-dimensions",
            Sum(dimensions.Select(d => d.Result.Metrics?.InputTokens ?? d.Result.InputTokens)),
            Sum(dimensions.Select(d => d.Result.Metrics?.OutputTokens ?? d.Result.OutputTokens)),
            Sum(dimensions.Select(d => d.Result.Metrics?.CacheReadTokens)),
            Sum(dimensions.Select(d => d.Result.Metrics?.CacheWriteTokens)),
            Sum(dimensions.Select(d => d.Result.Metrics?.ReasoningTokens)),
            Sum(dimensions.Select(d => d.Result.Metrics?.PremiumRequestCost)),
            wallClockDuration.TotalMilliseconds,
            dimensions.Sum(d => d.Result.Metrics?.TurnCount ?? 0),
            dimensions.Sum(d => d.Result.Metrics?.ToolCallCount ?? 0),
            dimensions.Sum(d => d.Result.Metrics?.FailedToolCallCount ?? 0),
            dimensions.Sum(d => d.Result.Metrics?.SessionErrorCount ?? 0),
            dimensions.All(d => d.Result.Metrics?.ReachedIdle == true),
            dimensions.Any(d => d.Result.Metrics?.WasAborted == true),
            DistinctIds(dimensions.SelectMany(d => d.Result.Metrics?.ProviderCallIds ?? [])),
            DistinctIds(dimensions.SelectMany(d => d.Result.Metrics?.ApiCallIds ?? [])),
            dimensions.SelectMany(d => d.Result.Metrics?.FailedToolCalls ?? []).ToArray());

    private static int? Sum(IEnumerable<int?> values)
    {
        var found = false;
        var total = 0;
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            found = true;
            total += value.Value;
        }

        return found ? total : null;
    }

    private static double? Sum(IEnumerable<double?> values)
    {
        var found = false;
        var total = 0.0;
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            found = true;
            total += value.Value;
        }

        return found ? total : null;
    }

    private static IReadOnlyList<string>? DistinctIds(IEnumerable<string> values)
    {
        var ids = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ids.Length == 0 ? null : ids;
    }

    private static string? ResolveRecommendedTier(IReadOnlyList<ReviewDimensionResult> dimensions)
    {
        var tiers = new[] { "small", "medium", "large" };
        var max = -1;
        foreach (var tier in dimensions.Select(d => d.Result.RecommendedModelTier))
        {
            var index = Array.FindIndex(tiers, item => item.Equals(tier, StringComparison.OrdinalIgnoreCase));
            if (index > max)
            {
                max = index;
            }
        }

        return max < 0 ? null : tiers[max];
    }

    private static string FormatTokens(int? inputTokens, int? outputTokens)
        => inputTokens.HasValue || outputTokens.HasValue
            ? $"{inputTokens?.ToString() ?? "?"}/{outputTokens?.ToString() ?? "?"}"
            : "n/a";

    private static string Summarize(StageResult result)
        => result.PolicyRationale
           ?? result.Evidence?.FirstOrDefault()?.Summary
           ?? result.Artifacts?.FirstOrDefault()?.Value
           ?? result.Error
           ?? "No summary supplied.";

    private static string EscapeTable(string value)
        => value.ReplaceLineEndings(" ").Replace("|", "\\|");
}
