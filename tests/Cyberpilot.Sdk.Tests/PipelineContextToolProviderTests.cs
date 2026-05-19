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
        Assert.Equal(17, response.Data.PullRequest?.Number);
        Assert.Equal("review", response.Data.CurrentStage);
        Assert.Contains("plan:approved", response.Data.KnownApprovals);
        var history = Assert.Single(response.Data.PriorStages);
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
        Assert.Equal("main", context.BaseBranch);
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
    public async Task GetPullRequestDetailsAsync_WithExplicitPrNumberDoesNotUseIssueNumber()
    {
        var context = CreateContext(prHeadBranch: "feature/issue-34", prNumber: 46);
        var cli = new FakeGitHubCli("""{"number":46}""");
        var provider = new PipelineContextToolProvider(context, Stage("review"), cli);

        var response = await provider.GetPullRequestDetailsAsync();

        Assert.True(response.Success);
        Assert.Contains("46", cli.LastArgs!);
        Assert.DoesNotContain("42", cli.LastArgs!);
        Assert.Equal(46, context.CreateStageContext("review").PullRequest?.Number);
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
                { "path": "web/Controllers/WeatherController.cs", "additions": 10, "deletions": 1, "status": "modified" },
                { "path": "tests/Cyberpilot.Sdk.Tests/WeatherTests.cs", "additions": 20, "deletions": 2, "status": "added" },
                { "path": "docs/weather.md", "additions": 60, "deletions": 9, "status": "modified" }
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
        Assert.Equal("web/Controllers/WeatherController.cs", response.Data.Files[0].Path);
        Assert.Equal("modified", response.Data.Files[0].Status);
        Assert.Equal("web", response.Data.Files[0].TopDirectory);
        Assert.Equal(".cs", response.Data.Files[0].Extension);
        Assert.Contains(response.Data.TopDirectories, group => group is { Name: "web", FileCount: 1, Additions: 10, Deletions: 1 });
        Assert.Contains(response.Data.Extensions, group => group is { Name: ".cs", FileCount: 2, Additions: 30, Deletions: 3 });
        Assert.Contains("production_code_changed", response.Data.Signals);
        Assert.Contains("test_code_changed", response.Data.Signals);
        Assert.Contains("documentation_changed", response.Data.Signals);
        Assert.Contains("web_surface_changed", response.Data.Signals);
        Assert.NotNull(response.DetailedOutput);
        var artifact = Assert.Single(context.GetToolArtifacts("docs"));
        Assert.Equal("tool-output-get_pr_diff_summary", artifact.Name);
        Assert.Contains("docs/weather.md", artifact.Value);
    }

    [Fact]
    public async Task GetPullRequestChecksAsync_ReturnsStructuredCheckSummary()
    {
        var context = CreateContext(captureToolOutputArtifacts: true);
        context.PrUrl = "https://github.com/owner/repo/pull/17";
        var raw = """
            [
              { "name": "CodeQL", "state": "pass", "bucket": "pass", "workflow": "CodeQL", "link": "https://github.com/checks/1" },
              { "name": "Unit Tests", "state": "pending", "bucket": "pending", "workflow": "CI" }
            ]
            """;
        var provider = new PipelineContextToolProvider(context, Stage("review"), new FakeGitHubCli(raw));

        var response = await provider.GetPullRequestChecksAsync();

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(17, response.Data.Number);
        Assert.False(response.Data.HasFailures);
        Assert.True(response.Data.HasPending);
        Assert.True(response.Data.HasCodeQl);
        Assert.Contains(response.Data.Checks, check => check.Name == "CodeQL" && check.Workflow == "CodeQL");
        var artifact = Assert.Single(context.GetToolArtifacts("review"));
        Assert.Equal("tool-output-get_pr_checks", artifact.Name);
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

    [Fact]
    public async Task RenderStageCommentAsync_ReturnsDeterministicReviewVerdictBody()
    {
        var context = CreateContext(prNumber: 17);
        var provider = new PipelineContextToolProvider(context, Stage("review"), new FakeGitHubCli());

        var response = await provider.RenderStageCommentAsync("verdict", "Approved after architecture, security, and test review.");

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal("review", response.Data.StageName);
        Assert.Equal("verdict", response.Data.CommentKind);
        Assert.Equal("PR #17", response.Data.Target);
        Assert.Equal("review-verdict", response.Data.SuggestedArtifactName);
        Assert.Equal("## 🎸 The Critic's Verdict", response.Data.Heading);
        Assert.Contains("**Target:** PR #17", response.Data.Body);
        Assert.Contains("Approved after architecture", response.Data.Body);
        Assert.Contains("do not post", response.Data.Usage);
    }

    [Fact]
    public async Task RenderStageCommentAsync_RejectsUnsupportedKind()
    {
        var provider = new PipelineContextToolProvider(CreateContext(), Stage("docs"), new FakeGitHubCli());

        var response = await provider.RenderStageCommentAsync("confetti", "Done.");

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal("unsupported_comment_kind", response.Error.Code);
    }

    [Fact]
    public async Task RenderStageCommentAsync_TruncatesLargeSummaries()
    {
        var provider = new PipelineContextToolProvider(CreateContext(prNumber: 17), Stage("review"), new FakeGitHubCli());

        var response = await provider.RenderStageCommentAsync("review-verdict", new string('x', 3_200));

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Contains("...[truncated]", response.Data.Body);
        Assert.True(response.Data.Body.Length < 3_100);
    }

    [Fact]
    public async Task GetChangedFileContentAsync_ReadsRepoRelativePathWithLineNumbers()
    {
        var repo = Directory.CreateTempSubdirectory("cyberpilot-sdk-test-");
        try
        {
            var filePath = Path.Combine(repo.FullName, "src", "Weather.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, "first line\nsecond line");
            var provider = new PipelineContextToolProvider(CreateContext(repoRoot: repo.FullName), Stage("review"), new FakeGitHubCli());

            var response = await provider.GetChangedFileContentAsync("src/Weather.cs");

            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Equal("src/Weather.cs", response.Data.Path);
            Assert.Equal(2, response.Data.LineCount);
            Assert.False(response.Data.Truncated);
            Assert.Contains("1. first line", response.Data.NumberedContent);
            Assert.Contains("2. second line", response.Data.NumberedContent);
        }
        finally
        {
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetChangedFileContentAsync_RejectsRootedPath()
    {
        var provider = new PipelineContextToolProvider(CreateContext(), Stage("review"), new FakeGitHubCli());

        var response = await provider.GetChangedFileContentAsync("C:\\temp\\Weather.cs");

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal("invalid_path", response.Error.Code);
    }

    [Fact]
    public async Task CollectValidationEvidenceAsync_RejectsUnsupportedValidationKind()
    {
        var provider = new PipelineContextToolProvider(CreateContext(), Stage("review"), new FakeGitHubCli());

        var response = await provider.CollectValidationEvidenceAsync("npm test", "App.csproj");

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal("unsupported_validation", response.Error.Code);
    }

    private static PipelineExecutionContext CreateContext(bool captureToolOutputArtifacts = false, string? prHeadBranch = null, int? prNumber = null, string? repoRoot = null)
    {
        return new PipelineExecutionContext(
            new CyberpilotOptions(
                42,
                repoRoot ?? Directory.GetCurrentDirectory(),
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
                PrHeadBranch: prHeadBranch,
                PrNumber: prNumber,
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
