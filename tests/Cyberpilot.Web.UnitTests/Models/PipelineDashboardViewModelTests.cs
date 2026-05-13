using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.UnitTests.Models;

public class PipelineDashboardViewModelTests
{
    [Fact]
    public void AvailableModels_IsNotEmpty()
    {
        Assert.NotEmpty(PipelineIssuesViewModel.AvailableModels);
    }

    [Fact]
    public void AvailableModels_ContainsClaude()
    {
        Assert.Contains("claude-sonnet-4.6", PipelineIssuesViewModel.AvailableModels);
    }

    [Fact]
    public void PipelineIssuesViewModel_ErrorConstructor_SetsEmptyCollections()
    {
        var vm = new PipelineIssuesViewModel([], "repo", "repo", null, "error", [], new HashSet<int>(), new Dictionary<int, string>());
        Assert.Empty(vm.SdkActiveIssueNumbers);
        Assert.Empty(vm.LatestSdkRunIds);
        Assert.Equal("error", vm.Error);
    }
}
