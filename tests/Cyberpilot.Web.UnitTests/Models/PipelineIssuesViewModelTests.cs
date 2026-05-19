using Cyberpilot.GitHub;
using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.UnitTests.Models;

public sealed class PipelineIssuesViewModelTests
{
    private static GitHubIssueSummary CreateIssue(int number = 1, params string[] labels)
        => new(number, "Test Issue", "https://github.com/test/1", labels, DateTimeOffset.UtcNow, "OPEN", false);

    private static PipelineIssuesViewModel CreateViewModel(
        IReadOnlySet<int>? sdkActiveIssueNumbers = null,
        IReadOnlyDictionary<int, string>? latestSdkRunIds = null)
        => new(
            Array.Empty<GitHubIssueSummary>(),
            "owner/repo",
            "owner/repo",
            null,
            null,
            [],
            sdkActiveIssueNumbers ?? new HashSet<int>(),
            latestSdkRunIds ?? new Dictionary<int, string>());

    [Fact]
    public void GetStatus_NoLabels_ReturnsNone()
    {
        var vm = CreateViewModel();
        var issue = CreateIssue();

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotStatus.None, status);
    }

    [Fact]
    public void GetStatus_CloudActiveLabel_ReturnsCloudActive()
    {
        var vm = CreateViewModel();
        var issue = CreateIssue(labels: "cloud/triage");

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotRunnerType.Cloud, status.Runner);
        Assert.Equal("triage", status.Stage);
        Assert.True(status.IsActive);
        Assert.False(status.IsDone);
        Assert.False(status.IsFailed);
    }

    [Fact]
    public void GetStatus_LocalActiveLabel_ReturnsLocalActive()
    {
        var vm = CreateViewModel();
        var issue = CreateIssue(labels: "local/plan");

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotRunnerType.Local, status.Runner);
        Assert.Equal("plan", status.Stage);
        Assert.True(status.IsActive);
    }

    [Fact]
    public void GetStatus_SdkActiveLabel_ReturnsSdkActive()
    {
        var vm = CreateViewModel();
        var issue = CreateIssue(labels: "sdk/implement");

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotRunnerType.Sdk, status.Runner);
        Assert.Equal("implement", status.Stage);
        Assert.True(status.IsActive);
    }

    [Fact]
    public void GetStatus_CloudDone_ReturnsDone()
    {
        var vm = CreateViewModel();
        var issue = CreateIssue(labels: "cloud/done");

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotRunnerType.Cloud, status.Runner);
        Assert.Equal("done", status.Stage);
        Assert.False(status.IsActive);
        Assert.True(status.IsDone);
        Assert.False(status.IsFailed);
    }

    [Fact]
    public void GetStatus_CloudFailed_ReturnsFailed()
    {
        var vm = CreateViewModel();
        var issue = CreateIssue(labels: "cloud/failed");

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotRunnerType.Cloud, status.Runner);
        Assert.Equal("failed", status.Stage);
        Assert.False(status.IsActive);
        Assert.False(status.IsDone);
        Assert.True(status.IsFailed);
    }

    [Fact]
    public void GetStatus_ActiveCloudTakesPriorityOverDoneSdk()
    {
        var vm = CreateViewModel();
        var issue = CreateIssue(labels: ["cloud/triage", "sdk/done"]);

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotRunnerType.Cloud, status.Runner);
        Assert.Equal("triage", status.Stage);
        Assert.True(status.IsActive);
    }

    [Fact]
    public void GetStatus_DoneCloudTakesPriorityOverDoneLocal()
    {
        var vm = CreateViewModel();
        var issue = CreateIssue(labels: ["cloud/done", "local/done"]);

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotRunnerType.Cloud, status.Runner);
        Assert.True(status.IsDone);
    }

    [Fact]
    public void GetStatus_SdkQueuedFromDb_WhenNoLabel()
    {
        var sdkActive = new HashSet<int> { 42 };
        var vm = CreateViewModel(sdkActiveIssueNumbers: sdkActive);
        var issue = CreateIssue(number: 42);

        var status = vm.GetStatus(issue);

        Assert.Equal(CyberpilotRunnerType.Sdk, status.Runner);
        Assert.Equal("queued", status.Stage);
        Assert.True(status.IsActive);
        Assert.False(status.IsDone);
        Assert.False(status.IsFailed);
    }

    [Fact]
    public void AvailableModelFamilies_IsNotEmpty()
    {
        var families = PipelineIssuesViewModel.AvailableModelFamilies;

        Assert.NotEmpty(families);
        Assert.Contains(families, f => f.ModelValue.StartsWith("claude-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(families, f => f.ModelValue.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase));
    }
}
