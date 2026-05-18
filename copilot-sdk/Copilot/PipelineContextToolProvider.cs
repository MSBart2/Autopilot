using System.Text.Json;
using Cyberpilot.GitHub;
using Cyberpilot.Pipeline;
using Microsoft.Extensions.AI;

namespace Cyberpilot.Copilot;

internal sealed class PipelineContextToolProvider(PipelineExecutionContext context, StageDefinition stage, IGitHubCli gitHubCli)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public ICollection<AIFunction> CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(
                (CancellationToken cancellationToken) => GetPipelineContextAsync(cancellationToken),
                "get_pipeline_context",
                "Returns compact Cyberpilot run context including issue, repository, branch, PR, stage history, and artifact summaries."),
            AIFunctionFactory.Create(
                (CancellationToken cancellationToken) => GetPullRequestDetailsAsync(cancellationToken),
                "get_pr_details",
                "Returns compact pull request metadata for the current Cyberpilot run."),
            AIFunctionFactory.Create(
                (int maxFiles, CancellationToken cancellationToken) => GetPullRequestDiffSummaryAsync(maxFiles, cancellationToken),
                "get_pr_diff_summary",
                "Returns a compact pull request diff summary and a reference to detailed persisted output."),
        ];
    }

    public Task<PipelineToolResponse<PipelineContextToolResult>> GetPipelineContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new PipelineContextToolResult(
            context.IssueNumber,
            context.Repository,
            context.RepoRoot,
            context.BranchName,
            context.HeadBranch,
            context.PrNumber,
            context.PrUrl,
            stage.Name,
            context.FinalStage,
            context.StageHistory.Select(StageHistoryToolItem.FromSummary).ToArray());

        return Task.FromResult(PipelineToolResponse<PipelineContextToolResult>.Ok(result));
    }

    public async Task<PipelineToolResponse<PullRequestDetailsToolResult>> GetPullRequestDetailsAsync(CancellationToken cancellationToken = default)
    {
        var prNumber = context.PrNumber;
        if (prNumber is null or <= 0)
        {
            return PipelineToolResponse<PullRequestDetailsToolResult>.Fail("missing_pr", "No pull request is known for this run. Create or link a PR before requesting PR details.");
        }

        try
        {
            var raw = await gitHubCli.RunAsync(
                ["pr", "view", prNumber.Value.ToString(), "--json", "number,title,state,url,headRefName,baseRefName,author,mergeable,reviewDecision,changedFiles,additions,deletions,labels,isDraft"],
                allowFailure: false,
                cancellationToken);
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var result = new PullRequestDetailsToolResult(
                ReadInt(root, "number") ?? prNumber.Value,
                ReadString(root, "title"),
                ReadString(root, "state"),
                ReadString(root, "url") ?? context.PrUrl,
                ReadString(root, "headRefName") ?? context.HeadBranch,
                ReadString(root, "baseRefName"),
                ReadAuthorLogin(root),
                ReadString(root, "mergeable"),
                ReadString(root, "reviewDecision"),
                ReadBool(root, "isDraft"),
                ReadInt(root, "changedFiles"),
                ReadInt(root, "additions"),
                ReadInt(root, "deletions"),
                ReadLabels(root));
            var reference = PersistToolOutput("get_pr_details", raw, "application/json");
            return PipelineToolResponse<PullRequestDetailsToolResult>.Ok(result, reference);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PipelineToolResponse<PullRequestDetailsToolResult>.Fail("pr_details_failed", $"Unable to load PR details for #{prNumber}: {ex.Message}");
        }
    }

    public async Task<PipelineToolResponse<PullRequestDiffSummaryToolResult>> GetPullRequestDiffSummaryAsync(int maxFiles = 40, CancellationToken cancellationToken = default)
    {
        var prNumber = context.PrNumber;
        if (prNumber is null or <= 0)
        {
            return PipelineToolResponse<PullRequestDiffSummaryToolResult>.Fail("missing_pr", "No pull request is known for this run. Create or link a PR before requesting a diff summary.");
        }

        var limit = Math.Clamp(maxFiles <= 0 ? 40 : maxFiles, 1, 100);
        try
        {
            var raw = await gitHubCli.RunAsync(
                ["pr", "view", prNumber.Value.ToString(), "--json", "number,url,changedFiles,additions,deletions,files"],
                allowFailure: false,
                cancellationToken);
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var files = ReadFiles(root).ToArray();
            var result = new PullRequestDiffSummaryToolResult(
                ReadInt(root, "number") ?? prNumber.Value,
                ReadString(root, "url") ?? context.PrUrl,
                ReadInt(root, "changedFiles") ?? files.Length,
                ReadInt(root, "additions"),
                ReadInt(root, "deletions"),
                files.Take(limit).ToArray(),
                files.Length > limit);
            var reference = PersistToolOutput("get_pr_diff_summary", raw, "application/json");
            return PipelineToolResponse<PullRequestDiffSummaryToolResult>.Ok(result, reference);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PipelineToolResponse<PullRequestDiffSummaryToolResult>.Fail("pr_diff_summary_failed", $"Unable to load PR diff summary for #{prNumber}: {ex.Message}");
        }
    }

    private ToolOutputReference? PersistToolOutput(string toolName, string rawOutput, string mediaType)
    {
        if (!context.Options.CaptureToolOutputArtifacts)
        {
            return null;
        }

        var artifactName = $"tool-output-{toolName}";
        var uri = $"cyberpilot://tool-output/{Uri.EscapeDataString(stage.Name)}/{Uri.EscapeDataString(toolName)}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        context.RecordToolArtifact(stage.Name, new StageArtifact(artifactName, Truncate(rawOutput, 3800), uri, mediaType));
        return new ToolOutputReference(artifactName, uri);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength), "...[truncated]");
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetString() : null;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : null;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;
    }

    private static string? ReadAuthorLogin(JsonElement element)
    {
        return element.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object ? ReadString(author, "login") : null;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement element)
    {
        if (!element.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return labels.EnumerateArray()
            .Select(label => ReadString(label, "name"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static IEnumerable<PullRequestFileSummary> ReadFiles(JsonElement element)
    {
        if (!element.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var file in files.EnumerateArray())
        {
            var path = ReadString(file, "path") ?? ReadString(file, "filename") ?? ReadString(file, "name");
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            yield return new PullRequestFileSummary(path, ReadInt(file, "additions"), ReadInt(file, "deletions"));
        }
    }
}

internal sealed record PipelineToolResponse<T>(bool Success, T? Data, PipelineToolError? Error, ToolOutputReference? DetailedOutput)
{
    public static PipelineToolResponse<T> Ok(T data, ToolOutputReference? detailedOutput = null) => new(true, data, null, detailedOutput);

    public static PipelineToolResponse<T> Fail(string code, string message) => new(false, default, new PipelineToolError(code, message), null);
}

internal sealed record PipelineToolError(string Code, string Message);

internal sealed record ToolOutputReference(string ArtifactName, string Uri);

internal sealed record PipelineContextToolResult(
    int IssueNumber,
    string? Repository,
    string RepoRoot,
    string? BranchName,
    string? HeadBranch,
    int? PullRequestNumber,
    string? PullRequestUrl,
    string CurrentStage,
    string FinalStage,
    IReadOnlyList<StageHistoryToolItem> StageHistory);

internal sealed record StageHistoryToolItem(string StageName, string Status, string Decision, string? Error, IReadOnlyList<string> Artifacts, IReadOnlyList<string> Evidence)
{
    public static StageHistoryToolItem FromSummary(StageExecutionSummary summary)
    {
        return new StageHistoryToolItem(summary.StageName, summary.Status, summary.Decision, summary.Error, summary.Artifacts, summary.Evidence);
    }
}

internal sealed record PullRequestDetailsToolResult(
    int Number,
    string? Title,
    string? State,
    string? Url,
    string? HeadRefName,
    string? BaseRefName,
    string? AuthorLogin,
    string? Mergeable,
    string? ReviewDecision,
    bool? IsDraft,
    int? ChangedFiles,
    int? Additions,
    int? Deletions,
    IReadOnlyList<string> Labels);

internal sealed record PullRequestDiffSummaryToolResult(
    int Number,
    string? Url,
    int ChangedFiles,
    int? Additions,
    int? Deletions,
    IReadOnlyList<PullRequestFileSummary> Files,
    bool Truncated);

internal sealed record PullRequestFileSummary(string Path, int? Additions, int? Deletions);
