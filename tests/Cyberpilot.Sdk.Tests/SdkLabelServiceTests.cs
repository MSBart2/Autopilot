using Cyberpilot.GitHub;

namespace Cyberpilot.Sdk.Tests;

public sealed class SdkLabelServiceTests
{
    [Fact]
    public async Task SetStageAsync_RemovesOnlySdkStageLabels()
    {
        var issueClient = new FakeIssueClient
        {
            IssueLabels = ["sdk", "sdk/planning", "bug", "local/triage"]
        };
        var service = new SdkLabelService(issueClient, TextWriter.Null);

        await service.SetStageAsync(122, "sdk/review");

        Assert.Equal(["sdk/planning"], issueClient.RemovedLabels);
        Assert.Equal(["sdk/review"], issueClient.AddedLabels);
    }

    [Fact]
    public async Task EnsureRequiredLabelsAsync_CreatesOnlyMissingLabels()
    {
        var issueClient = new FakeIssueClient
        {
            RepositoryLabels = new HashSet<string>(["sdk", "sdk/triage"], StringComparer.OrdinalIgnoreCase)
        };
        var service = new SdkLabelService(issueClient, TextWriter.Null);

        await service.EnsureRequiredLabelsAsync(createMissing: true);

        Assert.DoesNotContain("sdk", issueClient.CreatedLabels);
        Assert.Contains("sdk/done", issueClient.CreatedLabels);
        Assert.Contains("sdk/failed", issueClient.CreatedLabels);
    }

    [Fact]
    public async Task EnsureProvenanceAsync_AddsProvenanceLabel()
    {
        var issueClient = new FakeIssueClient();
        var service = new SdkLabelService(issueClient, TextWriter.Null);

        await service.EnsureProvenanceAsync(10);

        Assert.Contains("sdk", issueClient.AddedLabels);
    }

    [Fact]
    public async Task EnsureRequiredLabelsAsync_AllLabelsExist_PrintsMessage()
    {
        var issueClient = new FakeIssueClient
        {
            RepositoryLabels = new HashSet<string>(SdkLabelService.RequiredLabels, StringComparer.OrdinalIgnoreCase)
        };
        var output = new StringWriter();
        var service = new SdkLabelService(issueClient, output);

        await service.EnsureRequiredLabelsAsync(createMissing: true);

        Assert.Empty(issueClient.CreatedLabels);
        Assert.Contains("All SDK labels are present.", output.ToString());
    }

    [Fact]
    public async Task EnsureRequiredLabelsAsync_NotCreateMissing_ThrowsOnMissing()
    {
        var issueClient = new FakeIssueClient
        {
            RepositoryLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
        var service = new SdkLabelService(issueClient, TextWriter.Null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureRequiredLabelsAsync(createMissing: false));
    }

    [Fact]
    public async Task SetStageAsync_NoExistingStageLabels_JustAddsNew()
    {
        var issueClient = new FakeIssueClient
        {
            IssueLabels = ["bug", "enhancement"]
        };
        var service = new SdkLabelService(issueClient, TextWriter.Null);

        await service.SetStageAsync(5, "sdk/triage");

        Assert.Empty(issueClient.RemovedLabels);
        Assert.Contains("sdk/triage", issueClient.AddedLabels);
    }

    [Fact]
    public async Task ClearStageAsync_RemovesSdkStageLabelsButLeavesProvenance()
    {
        var issueClient = new FakeIssueClient
        {
            IssueLabels = ["sdk", "sdk/triage", "bug"]
        };
        var service = new SdkLabelService(issueClient, TextWriter.Null);

        await service.ClearStageAsync(5);

        Assert.Equal(["sdk/triage"], issueClient.RemovedLabels);
        Assert.Empty(issueClient.AddedLabels);
    }

    private sealed class FakeIssueClient : IGitHubIssueClient
    {
        public IReadOnlyList<string> IssueLabels { get; init; } = [];
        public IReadOnlySet<string> RepositoryLabels { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> AddedLabels { get; } = [];
        public List<string> RemovedLabels { get; } = [];
        public List<string> CreatedLabels { get; } = [];

        public Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default)
        {
            AddedLabels.Add(label);
            return Task.CompletedTask;
        }

        public Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueComment>>([]);
        public Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<GitHubIssueSummary?>(new GitHubIssueSummary(issueNumber, "Test issue", string.Empty, IssueLabels, DateTimeOffset.UtcNow, "OPEN", false));
        }

        public Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IssueLabels);
        }

        public Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("OPEN");
        }

        public Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RepositoryLabels);
        }

        public Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GitHubIssueSummary>>([]);
        }

        public Task<IReadOnlyList<GitHubIssueSummary>> ListOpenPullRequestsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GitHubIssueSummary>>([]);
        }

        public Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default)
        {
            RemovedLabels.Add(label);
            return Task.CompletedTask;
        }

        public Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default)
        {
            CreatedLabels.Add(label);
            return Task.CompletedTask;
        }

        public Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubPullRequestInfo?>(null);
    }
}
