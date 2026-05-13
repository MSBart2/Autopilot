using Cyberpilot.GitHub;

namespace Cyberpilot.Sdk.Tests;

public sealed class GitHubIssueClientTests
{
    [Fact]
    public async Task AddIssueLabelAsync_CallsCliWithCorrectArgs()
    {
        var cli = new FakeGitHubCli();
        var client = new GitHubIssueClient(cli);
        await client.AddIssueLabelAsync(42, "bug");
        Assert.NotNull(cli.LastArgs);
        Assert.Contains(cli.LastArgs, a => a == "issue");
        Assert.Contains(cli.LastArgs, a => a == "edit");
        Assert.Contains(cli.LastArgs, a => a == "42");
        Assert.Contains(cli.LastArgs, a => a == "--add-label");
        Assert.Contains(cli.LastArgs, a => a == "bug");
    }

    [Fact]
    public async Task GetIssueLabelsAsync_ParsesLabels()
    {
        var cli = new FakeGitHubCli { Output = "bug\nenhancement\nhelp wanted" };
        var client = new GitHubIssueClient(cli);
        var labels = await client.GetIssueLabelsAsync(10);
        Assert.Equal(3, labels.Count);
        Assert.Contains("bug", labels);
        Assert.Contains("enhancement", labels);
    }

    [Fact]
    public async Task GetIssueStateAsync_ReturnsState()
    {
        var cli = new FakeGitHubCli { Output = "OPEN\n" };
        var client = new GitHubIssueClient(cli);
        var state = await client.GetIssueStateAsync(5);
        Assert.Equal("OPEN", state);
    }

    [Fact]
    public async Task GetIssueAsync_ReturnsIssueSummary()
    {
        var cli = new SequentialGitHubCli(["Test Title\n", "Issue body\n", "OPEN\n"]);
        var client = new GitHubIssueClient(cli);
        var issue = await client.GetIssueAsync(7);
        Assert.NotNull(issue);
        Assert.Equal(7, issue.Number);
        Assert.Equal("Test Title", issue.Title);
        Assert.Equal("Issue body", issue.Body);
        Assert.Equal("OPEN", issue.State);
    }

    [Fact]
    public async Task GetRepositoryLabelsAsync_ReturnsSet()
    {
        var cli = new FakeGitHubCli { Output = "bug\nenhancement\nbug" };
        var client = new GitHubIssueClient(cli);
        var labels = await client.GetRepositoryLabelsAsync();
        Assert.Contains("bug", labels);
        Assert.Contains("enhancement", labels);
    }

    [Fact]
    public async Task ListOpenIssuesAsync_ParsesJson()
    {
        var json = """[{"number":1,"title":"First","body":"Details","url":"https://github.com/test/1","labels":[{"name":"bug"}],"updatedAt":"2024-01-01T00:00:00Z"}]""";
        var cli = new FakeGitHubCli { Output = json };
        var client = new GitHubIssueClient(cli);
        var issues = await client.ListOpenIssuesAsync();
        Assert.Single(issues);
        Assert.Equal("First", issues[0].Title);
        Assert.Equal("Details", issues[0].Body);
    }

    [Fact]
    public async Task RemoveIssueLabelAsync_CallsCliWithAllowFailure()
    {
        var cli = new FakeGitHubCli();
        var client = new GitHubIssueClient(cli);
        await client.RemoveIssueLabelAsync(42, "old-label");
        Assert.True(cli.LastAllowFailure);
    }

    [Fact]
    public async Task CommentAsync_CallsCliWithBody()
    {
        var cli = new FakeGitHubCli();
        var client = new GitHubIssueClient(cli);
        await client.CommentAsync(10, "Hello!");
        Assert.NotNull(cli.LastArgs);
        Assert.Contains(cli.LastArgs, a => a == "--body");
        Assert.Contains(cli.LastArgs, a => a == "Hello!");
    }

    [Fact]
    public async Task CreateOrUpdateLabelAsync_CallsLabelCreate()
    {
        var cli = new FakeGitHubCli();
        var client = new GitHubIssueClient(cli);
        await client.CreateOrUpdateLabelAsync("sdk", "5319e7", "SDK label");
        Assert.NotNull(cli.LastArgs);
        Assert.Contains(cli.LastArgs, a => a == "label");
        Assert.Contains(cli.LastArgs, a => a == "create");
        Assert.Contains(cli.LastArgs, a => a == "--force");
    }

    private sealed class FakeGitHubCli : IGitHubCli
    {
        public string Output { get; set; } = string.Empty;
        public IReadOnlyList<string>? LastArgs { get; private set; }
        public bool LastAllowFailure { get; private set; }

        public Task<string> RunAsync(IReadOnlyList<string> args, bool allowFailure = false, CancellationToken cancellationToken = default)
        {
            LastArgs = args.ToList();
            LastAllowFailure = allowFailure;
            return Task.FromResult(Output);
        }
    }

    private sealed class SequentialGitHubCli(string[] outputs) : IGitHubCli
    {
        private int callIndex;

        public Task<string> RunAsync(IReadOnlyList<string> args, bool allowFailure = false, CancellationToken cancellationToken = default)
        {
            var output = callIndex < outputs.Length ? outputs[callIndex] : string.Empty;
            callIndex++;
            return Task.FromResult(output);
        }
    }
}
