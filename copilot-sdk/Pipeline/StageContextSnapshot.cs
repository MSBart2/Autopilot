using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cyberpilot.Pipeline;

internal sealed record StageContextSnapshot(
    [property: JsonPropertyName("run_id")] string? RunId,
    [property: JsonPropertyName("issue_number")] int IssueNumber,
    [property: JsonPropertyName("repository")] string? Repository,
    [property: JsonPropertyName("repo_root")] string RepoRoot,
    [property: JsonPropertyName("pipeline_definition")] string PipelineDefinition,
    [property: JsonPropertyName("pipeline_version")] string PipelineVersion,
    [property: JsonPropertyName("current_stage")] string CurrentStage,
    [property: JsonPropertyName("branch_name")] string? BranchName,
    [property: JsonPropertyName("head_branch")] string? HeadBranch,
    [property: JsonPropertyName("base_branch")] string? BaseBranch,
    [property: JsonPropertyName("pull_request")] StageContextPullRequest? PullRequest,
    [property: JsonPropertyName("prior_stages")] IReadOnlyList<StageContextStageSummary> PriorStages,
    [property: JsonPropertyName("known_approvals")] IReadOnlyList<string> KnownApprovals)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static StageContextSnapshot Create(string stageName, PipelineExecutionContext context)
    {
        var priorStages = FilterPriorStages(stageName, context.StageHistory)
            .Select(StageContextStageSummary.FromSummary)
            .ToArray();

        return new StageContextSnapshot(
            context.RunId,
            context.IssueNumber,
            context.Repository,
            context.RepoRoot,
            context.Definition.Name,
            context.Definition.Version.Value,
            stageName,
            ShouldIncludeBranch(stageName) ? context.BranchName : null,
            ShouldIncludeBranch(stageName) ? context.HeadBranch : null,
            ShouldIncludePullRequest(stageName) ? context.BaseBranch : null,
            ShouldIncludePullRequest(stageName) ? StageContextPullRequest.Create(context) : null,
            priorStages,
            priorStages
                .Where(stage => stage.Decision.Equals("approved", StringComparison.OrdinalIgnoreCase))
                .Select(stage => $"{stage.StageName}:approved")
                .ToArray());
    }

    public string ToCompactJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static bool ShouldIncludeBranch(string stageName)
    {
        return !stageName.Equals("triage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludePullRequest(string stageName)
    {
        return stageName.Equals("review", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("docs", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("summary", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("deliver", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<StageExecutionSummary> FilterPriorStages(string stageName, IReadOnlyList<StageExecutionSummary> summaries)
    {
        var includedStages = stageName.ToLowerInvariant() switch
        {
            "plan" => new[] { "triage" },
            "implement" => ["triage", "plan", "review"],
            "review" => ["plan", "implement"],
            "docs" => ["implement", "review"],
            "summary" => ["implement", "review", "docs"],
            "deliver" => ["review", "docs", "summary"],
            _ => [],
        };

        return summaries
            .Where(summary => includedStages.Contains(summary.StageName, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }
}

internal sealed record StageContextPullRequest(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("head_branch")] string? HeadBranch,
    [property: JsonPropertyName("base_branch")] string? BaseBranch)
{
    public static StageContextPullRequest? Create(PipelineExecutionContext context)
    {
        if (context.PullRequestNumber is null
            && string.IsNullOrWhiteSpace(context.PrUrl)
            && string.IsNullOrWhiteSpace(context.HeadBranch)
            && string.IsNullOrWhiteSpace(context.BaseBranch))
        {
            return null;
        }

        return new StageContextPullRequest(context.PullRequestNumber, context.PrUrl, context.HeadBranch, context.BaseBranch);
    }
}

internal sealed record StageContextStageSummary(
    [property: JsonPropertyName("stage_name")] string StageName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("recommended_model_tier")] string? RecommendedModelTier,
    [property: JsonPropertyName("artifacts")] IReadOnlyList<string> Artifacts,
    [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence,
    [property: JsonPropertyName("required_actions")] IReadOnlyList<string>? RequiredActions)
{
    public static StageContextStageSummary FromSummary(StageExecutionSummary summary)
    {
        return new StageContextStageSummary(
            summary.StageName,
            summary.Status,
            summary.Decision,
            summary.Error,
            summary.RecommendedModelTier,
            summary.Artifacts,
            summary.Evidence,
            summary.RequiredActions);
    }
}
