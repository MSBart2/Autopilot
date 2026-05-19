using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed class PipelineExecutionContext(CyberpilotOptions options, PipelineDefinition definition)
{
    private readonly Dictionary<string, List<StageArtifact>> toolArtifacts = new(StringComparer.OrdinalIgnoreCase);

    public CyberpilotOptions Options { get; } = options;

    public PipelineDefinition Definition { get; } = definition;

    public string FinalStage { get; set; } = "not-started";

    public string? RunId => Options.RunId;

    public string? BranchName { get; set; }

    public string? PrUrl { get; set; }

    public string? BaseBranch { get; set; }

    public int? KnownPullRequestNumber { get; set; }

    public List<StageResult> StageResults { get; } = [];

    public List<StageExecutionSummary> StageHistory { get; } = [];

    public int IssueNumber => Options.IssueNumber;

    public string RepoRoot => Options.RepoRoot;

    public string? Repository => Options.Repository;

    public string? HeadBranch => string.IsNullOrWhiteSpace(Options.PrHeadBranch) ? BranchName : Options.PrHeadBranch;

    public int? PrNumber => TryParsePullRequestNumber(PrUrl);

    public int? PullRequestNumber => KnownPullRequestNumber ?? PrNumber ?? Options.PrNumber;

    public void RecordToolArtifact(string stageName, StageArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        ArgumentNullException.ThrowIfNull(artifact);

        if (!toolArtifacts.TryGetValue(stageName, out var artifacts))
        {
            artifacts = [];
            toolArtifacts[stageName] = artifacts;
        }

        artifacts.Add(artifact);
    }

    public IReadOnlyList<StageArtifact> GetToolArtifacts(string stageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        return toolArtifacts.TryGetValue(stageName, out var artifacts) ? artifacts.ToArray() : [];
    }

    public void RecordStageResult(string stageName, StageResult result)
    {
        StageResults.Add(result);
        StageHistory.Add(StageExecutionSummary.FromResult(stageName, result));
    }

    internal StageContextSnapshot CreateStageContext(string stageName)
    {
        return StageContextSnapshot.Create(stageName, this);
    }

    private static int? TryParsePullRequestNumber(string? prUrl)
    {
        if (string.IsNullOrWhiteSpace(prUrl))
        {
            return null;
        }

        var marker = "/pull/";
        var index = prUrl.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var value = prUrl[(index + marker.Length)..].Trim('/');
        return int.TryParse(value, out var number) ? number : null;
    }
}

internal sealed record StageExecutionSummary(
    string StageName,
    string Status,
    string Decision,
    string? Error,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<string> Evidence)
{
    public static StageExecutionSummary FromResult(string stageName, StageResult result)
    {
        return new StageExecutionSummary(
            stageName,
            result.Status,
            result.Decision,
            result.Error,
            result.Artifacts?.Select(artifact => FormatArtifact(artifact)).ToArray() ?? [],
            result.Evidence?.Select(evidence => FormatEvidence(evidence)).ToArray() ?? []);
    }

    private static string FormatArtifact(StageArtifact artifact)
    {
        return string.IsNullOrWhiteSpace(artifact.Value) ? artifact.Name : $"{artifact.Name}: {artifact.Value}";
    }

    private static string FormatEvidence(StageEvidence evidence)
    {
        return $"{evidence.Name}: {evidence.Summary}";
    }
}
