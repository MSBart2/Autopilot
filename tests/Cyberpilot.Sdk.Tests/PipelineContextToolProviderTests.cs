using Cyberpilot.Copilot;
using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class PipelineContextToolProviderTests
{
    [Fact]
    public async Task GetPipelineContextAsync_ReturnsHarnessOwnedState()
    {
        var context = CreateContext();
        context.BranchName = "feature/issue-42-review";
        context.PrUrl = "https://github.com/owner/repo/pull/17";
        context.RecordStageResult("plan", new StageResult(
            "GO",
            "approved",
            true,
            null,
            Artifacts: [new StageArtifact("plan-comment", "Plan ready.")],
            Evidence: [new StageEvidence("policy", "Strict policy satisfied.")]));
        var provider = new PipelineContextToolProvider(context, Stage("review"), new FakeGitHubCli());

        var response = await provider.GetPipelineContextAsync();

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(42, response.Data.IssueNumber);
        Assert.Equal("owner/repo", response.Data.Repository);
        Assert.Equal("feature/issue-42-review", response.Data.BranchName);
        Assert.Equal(17, response.Data.PullRequestNumber);
        Assert.Equal("review", response.Data.CurrentStage);
        var history = Assert.Single(response.Data.StageHistory);
        Assert.Equal("plan", history.StageName);
        Assert.Contains("plan-comment: Plan ready.", history.Artifacts);
    }

    [Fact]
    public async Task GetPullRequestDetailsAsync_ReturnsCompactDetailsAndPersistsRawOutputReference()
    {
        var context = CreateContext(captureToolOutputArtifacts: true);
        context.PrUrl = "https://github.com/owner/repo/pull/17";
        var raw = """
            {
              "number": 17,
              "title": "Improve review flow",
              "state": "OPEN",
              "url": "https://github.com/owner/repo/pull/17",
              "headRefName": "feature/issue-42-review",
              "baseRefName": "main",
              "author": { "login": "alice" },
              "mergeable": "MERGEABLE",
              "reviewDecision": "APPROVED",
              "isDraft": false,
              "changedFiles": 3,
              "additions": 25,
              "deletions": 4,
              "labels": [{ "name": "sdk/review" }]
            }
            """;
        var provider = new PipelineContextToolProvider(context, Stage("review"), new FakeGitHubCli(raw));

        var response = await provider.GetPullRequestDetailsAsync();

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(17, response.Data.Number);
        Assert.Equal("Improve review flow", response.Data.Title);
        Assert.Equal("OPEN", response.Data.State);
        Assert.Equal("alice", response.Data.AuthorLogin);
        Assert.Equal("sdk/review", Assert.Single(response.Data.Labels));
        Assert.NotNull(response.DetailedOutput);
        Assert.Equal("tool-output-get_pr_details", response.DetailedOutput.ArtifactName);
        var artifact = Assert.Single(context.GetToolArtifacts("review"));
        Assert.Equal("tool-output-get_pr_details", artifact.Name);
        Assert.Equal("application/json", artifact.MediaType);
        Assert.Contains("Improve review flow", artifact.Value);
    }

    [Fact]
    public async Task GetPullRequestDetailsAsync_WithoutKnownPrReturnsStructuredError()
    {
        var provider = new PipelineContextToolProvider(CreateContext(), Stage("review"), new FakeGitHubCli());

        var response = await provider.GetPullRequestDetailsAsync();

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal("missing_pr", response.Error.Code);
        Assert.Contains("No pull request is known", response.Error.Message);
    }

    [Fact]
    public async Task GetPullRequestDiffSummaryAsync_ReturnsLimitedFilesAndDetailedOutputReference()
    {
        var context = CreateContext(captureToolOutputArtifacts: true);
        context.PrUrl = "https://github.com/owner/repo/pull/17";
        var raw = """
            {
              "number": 17,
              "url": "https://github.com/owner/repo/pull/17",
              "changedFiles": 3,
              "additions": 90,
              "deletions": 12,
              "files": [
                { "path": "a.cs", "additions": 10, "deletions": 1 },
                { "path": "b.cs", "additions": 20, "deletions": 2 },
                { "path": "c.cs", "additions": 60, "deletions": 9 }
              ]
            }
            """;
        var provider = new PipelineContextToolProvider(context, Stage("docs"), new FakeGitHubCli(raw));

        var response = await provider.GetPullRequestDiffSummaryAsync(maxFiles: 2);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(3, response.Data.ChangedFiles);
        Assert.Equal(2, response.Data.Files.Count);
        Assert.True(response.Data.Truncated);
        Assert.Equal("a.cs", response.Data.Files[0].Path);
        Assert.NotNull(response.DetailedOutput);
        var artifact = Assert.Single(context.GetToolArtifacts("docs"));
        Assert.Equal("tool-output-get_pr_diff_summary", artifact.Name);
        Assert.Contains("c.cs", artifact.Value);
    }

    [Fact]
    public async Task GetPullRequestDetailsAsync_ByDefaultDoesNotPersistRawOutputReference()
    {
        var context = CreateContext();
        context.PrUrl = "https://github.com/owner/repo/pull/17";
        var provider = new PipelineContextToolProvider(context, Stage("review"), new FakeGitHubCli("""{"number":17}"""));

        var response = await provider.GetPullRequestDetailsAsync();

        Assert.True(response.Success);
        Assert.Null(response.DetailedOutput);
        Assert.Empty(context.GetToolArtifacts("review"));
    }

    private static PipelineExecutionContext CreateContext(bool captureToolOutputArtifacts = false)
    {
        return new PipelineExecutionContext(
            new CyberpilotOptions(
                42,
                Directory.GetCurrentDirectory(),
                "owner/repo",
                CyberpilotOptions.DefaultModel,
                false,
                false,
                false,
                false,
                CyberpilotOptions.DefaultStageTimeout,
                true,
                false,
                null,
                null,
                false,
                RuntimePreferences: new CyberpilotRuntimePreferences(CaptureToolOutputArtifacts: captureToolOutputArtifacts)),
            DefaultPipelineDefinitionProvider.Definition);
    }

    private static StageDefinition Stage(string name)
        => new(name.ToUpperInvariant(), name, $"{name}.agent.md", $"sdk/{name}");

    private sealed class FakeGitHubCli(string output = "{}") : IGitHubCli
    {
        public IReadOnlyList<string>? LastArgs { get; private set; }

        public Task<string> RunAsync(IReadOnlyList<string> args, bool allowFailure = false, CancellationToken cancellationToken = default)
        {
            LastArgs = args;
            return Task.FromResult(output);
        }
    }
}
