using Cyberpilot.Persistence;
using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.UnitTests.Models;

public class PipelineRunDetailsViewModelTests
{
    [Fact]
    public void Constructor_WithLabels_SetsAllProperties()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var logs = new[] { new PipelineStageLog { RunId = run.Id, StageName = "triage", Status = "GO" } };
        IReadOnlyList<string> labels = ["bug", "sdk"];

        var vm = new PipelineRunDetailsViewModel(run, logs, labels);

        Assert.Same(run, vm.Run);
        Assert.Equal(1, vm.Logs.Count);
        Assert.Equal(2, vm.Labels.Count);
    }

    [Fact]
    public void Constructor_WithoutLabels_DefaultsToEmpty()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var logs = Array.Empty<PipelineStageLog>();

        var vm = new PipelineRunDetailsViewModel(run, logs);

        Assert.Empty(vm.Labels);
    }
}
