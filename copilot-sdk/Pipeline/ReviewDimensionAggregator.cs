using System.Text;

namespace Cyberpilot.Pipeline;

internal static class ReviewDimensionAggregator
{
    public static StageResult Aggregate(int cycle, TimeSpan wallClockDuration, IReadOnlyList<ReviewDimensionResult> dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);

        var classification = Classify(dimensions);
        var status = classification.HasFailures ? StageStatus.Stop : StageStatus.Go;
        var decision = classification.HasFailures || classification.RequestedChanges.Count > 0
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
            BuildError(classification),
            InputTokens: Sum(dimensions.Select(d => d.Result.InputTokens)),
            OutputTokens: Sum(dimensions.Select(d => d.Result.OutputTokens)),
            Metrics: AggregateMetrics(dimensions, wallClockDuration),
            Artifacts: artifacts,
            Evidence: evidence,
            PolicyRationale: BuildPolicyRationale(classification),
            RequiredActions: requiredActions.Length == 0 ? null : requiredActions,
            RecommendedModelTier: ResolveRecommendedTier(dimensions));
    }

    private static DimensionClassification Classify(IReadOnlyList<ReviewDimensionResult> dimensions)
        => new(
            dimensions.Where(dimension => !dimension.Result.IsValid).ToArray(),
            dimensions.Where(dimension => dimension.Result.IsValid && !StageStatus.IsGo(dimension.Result)).ToArray(),
            dimensions
                .Where(dimension => dimension.Result.IsValid)
                .Where(dimension => StageStatus.IsGo(dimension.Result))
                .Where(dimension => StageDecision.RequestsChanges(dimension.Result))
                .ToArray());

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

    private static string? BuildError(DimensionClassification classification)
    {
        if (!classification.HasFailures)
        {
            return null;
        }

        var parts = new List<string>();
        if (classification.InvalidDimensions.Count > 0)
        {
            parts.Add($"invalid: {FormatDimensionIds(classification.InvalidDimensions)}");
        }

        if (classification.NonGoDimensions.Count > 0)
        {
            parts.Add($"non-GO: {FormatDimensionIds(classification.NonGoDimensions)}");
        }

        return $"Review dimensions failed closed ({string.Join("; ", parts)}).";
    }

    private static string BuildPolicyRationale(DimensionClassification classification)
    {
        if (classification.InvalidDimensions.Count > 0)
        {
            return "Parallel review fails closed when any read-only review dimension cannot produce a valid structured result, while preserving completed dimension output for diagnosis.";
        }

        if (classification.NonGoDimensions.Count > 0)
        {
            return "Parallel review fails closed when any read-only review dimension returns a non-GO status, while preserving completed dimension output for diagnosis.";
        }

        if (classification.RequestedChanges.Count > 0)
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
            Model: "parallel-review-dimensions",
            InputTokens: SumMetric(dimensions, metrics => metrics.InputTokens, result => result.InputTokens),
            OutputTokens: SumMetric(dimensions, metrics => metrics.OutputTokens, result => result.OutputTokens),
            CacheReadTokens: SumMetric(dimensions, metrics => metrics.CacheReadTokens),
            CacheWriteTokens: SumMetric(dimensions, metrics => metrics.CacheWriteTokens),
            ReasoningTokens: SumMetric(dimensions, metrics => metrics.ReasoningTokens),
            PremiumRequestCost: SumMetric(dimensions, metrics => metrics.PremiumRequestCost),
            DurationMs: wallClockDuration.TotalMilliseconds,
            TurnCount: SumCount(dimensions, metrics => metrics.TurnCount),
            ToolCallCount: SumCount(dimensions, metrics => metrics.ToolCallCount),
            FailedToolCallCount: SumCount(dimensions, metrics => metrics.FailedToolCallCount),
            SessionErrorCount: SumCount(dimensions, metrics => metrics.SessionErrorCount),
            ReachedIdle: dimensions.All(dimension => dimension.Result.Metrics?.ReachedIdle == true),
            WasAborted: dimensions.Any(dimension => dimension.Result.Metrics?.WasAborted == true),
            ProviderCallIds: DistinctIds(dimensions.SelectMany(dimension => dimension.Result.Metrics?.ProviderCallIds ?? [])),
            ApiCallIds: DistinctIds(dimensions.SelectMany(dimension => dimension.Result.Metrics?.ApiCallIds ?? [])),
            FailedToolCalls: dimensions.SelectMany(dimension => dimension.Result.Metrics?.FailedToolCalls ?? []).ToArray());

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

    private static int? SumMetric(
        IReadOnlyList<ReviewDimensionResult> dimensions,
        Func<StageExecutionMetrics, int?> metricValue,
        Func<StageResult, int?>? fallbackValue = null)
    {
        return Sum(dimensions.Select(dimension =>
            dimension.Result.Metrics is null
                ? fallbackValue?.Invoke(dimension.Result)
                : metricValue(dimension.Result.Metrics) ?? fallbackValue?.Invoke(dimension.Result)));
    }

    private static double? SumMetric(
        IReadOnlyList<ReviewDimensionResult> dimensions,
        Func<StageExecutionMetrics, double?> metricValue)
    {
        return Sum(dimensions.Select(dimension =>
            dimension.Result.Metrics is null
                ? null
                : metricValue(dimension.Result.Metrics)));
    }

    private static int SumCount(
        IReadOnlyList<ReviewDimensionResult> dimensions,
        Func<StageExecutionMetrics, int> metricValue)
    {
        return dimensions.Sum(dimension =>
            dimension.Result.Metrics is null
                ? 0
                : metricValue(dimension.Result.Metrics));
    }

    private static string FormatDimensionIds(IEnumerable<ReviewDimensionResult> dimensions)
        => string.Join(", ", dimensions.Select(dimension => dimension.Dimension.Id));

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

    private sealed record DimensionClassification(
        IReadOnlyList<ReviewDimensionResult> InvalidDimensions,
        IReadOnlyList<ReviewDimensionResult> NonGoDimensions,
        IReadOnlyList<ReviewDimensionResult> RequestedChanges)
    {
        public bool HasFailures => InvalidDimensions.Count > 0 || NonGoDimensions.Count > 0;
    }
}
