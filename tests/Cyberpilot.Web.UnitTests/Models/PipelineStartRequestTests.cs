using Cyberpilot.Pipeline;
using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.UnitTests.Models;

public class PipelineStartRequestTests
{
    [Fact]
    public void DefaultModel_IsClaude()
    {
        var request = new PipelineStartRequest();
        Assert.Equal("claude-sonnet-4.6", request.Model);
    }

    [Fact]
    public void DefaultSkipDeliver_IsFalse()
    {
        var request = new PipelineStartRequest();
        Assert.False(request.SkipDeliver);
    }

    [Fact]
    public void DefaultStageTimeoutMinutes_IsTwenty()
    {
        var request = new PipelineStartRequest();
        Assert.Equal(20, request.StageTimeoutMinutes);
    }

    [Fact]
    public void DefaultAllowMissingDocs_IsFalse()
    {
        var request = new PipelineStartRequest();
        Assert.False(request.AllowMissingDocs);
    }

    [Fact]
    public void DefaultPipelineDefinitionName_IsDefaultDefinition()
    {
        var request = new PipelineStartRequest();
        Assert.Equal(PipelineDefinitionDefaults.DefinitionName, request.PipelineDefinitionName);
    }

    [Fact]
    public void DefaultPolicyProfileName_IsStandard()
    {
        var request = new PipelineStartRequest();
        Assert.Equal(PipelineDefinitionDefaults.PolicyProfileName, request.PolicyProfileName);
    }
}
