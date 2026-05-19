using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.UnitTests.Models;

public class PipelineDashboardViewModelTests
{
    [Fact]
    public void AvailableModelFamilies_IsNotEmpty()
    {
        Assert.NotEmpty(PipelineIssuesViewModel.AvailableModelFamilies);
    }

    [Fact]
    public void AvailableModelFamilies_ContainsClaude()
    {
        Assert.Contains(PipelineIssuesViewModel.AvailableModelFamilies, f => f.ModelValue.StartsWith("claude-", StringComparison.OrdinalIgnoreCase));
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
