using Cyberpilot.Web.Hubs;

namespace Cyberpilot.Web.UnitTests.Hubs;

public class PipelineHubTests
{
    [Fact]
    public void GroupName_ReturnsExpectedFormat()
    {
        Assert.Equal("pipeline-run:abc123", PipelineHub.GroupName("abc123"));
    }

    [Fact]
    public void GroupName_EmptyId_ReturnsPrefix()
    {
        Assert.Equal("pipeline-run:", PipelineHub.GroupName(""));
    }
}
