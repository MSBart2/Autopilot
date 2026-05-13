using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class StageCatalogTests
{
    [Fact]
    public void Triage_HasCorrectProperties()
    {
        Assert.Equal("TRIAGE", StageCatalog.Triage.DisplayName);
        Assert.Equal("triage", StageCatalog.Triage.Name);
        Assert.Equal("triage.agent.md", StageCatalog.Triage.PromptFile);
        Assert.Equal("sdk/triage", StageCatalog.Triage.Label);
    }

    [Fact]
    public void Plan_HasCorrectProperties()
    {
        Assert.Equal("PLAN", StageCatalog.Plan.DisplayName);
        Assert.Equal("plan", StageCatalog.Plan.Name);
        Assert.Equal("plan.agent.md", StageCatalog.Plan.PromptFile);
        Assert.Equal("sdk/planning", StageCatalog.Plan.Label);
    }

    [Fact]
    public void Implement_HasCorrectProperties()
    {
        Assert.Equal("IMPLEMENT", StageCatalog.Implement.DisplayName);
        Assert.Equal("implement", StageCatalog.Implement.Name);
        Assert.Equal("implement.agent.md", StageCatalog.Implement.PromptFile);
        Assert.Equal("sdk/implementing", StageCatalog.Implement.Label);
    }

    [Fact]
    public void Review_HasCorrectProperties()
    {
        Assert.Equal("REVIEW", StageCatalog.Review.DisplayName);
        Assert.Equal("review", StageCatalog.Review.Name);
        Assert.Equal("pipeline-review.agent.md", StageCatalog.Review.PromptFile);
        Assert.Equal("sdk/review", StageCatalog.Review.Label);
    }

    [Fact]
    public void Docs_HasCorrectProperties()
    {
        Assert.Equal("DOCS", StageCatalog.Docs.DisplayName);
        Assert.Equal("docs", StageCatalog.Docs.Name);
        Assert.Equal("docs.agent.md", StageCatalog.Docs.PromptFile);
        Assert.Equal("sdk/docs", StageCatalog.Docs.Label);
    }

    [Fact]
    public void Deliver_HasCorrectProperties()
    {
        Assert.Equal("LAND", StageCatalog.Deliver.DisplayName);
        Assert.Equal("deliver", StageCatalog.Deliver.Name);
        Assert.Equal("deliver.agent.md", StageCatalog.Deliver.PromptFile);
        Assert.Equal("sdk/delivering", StageCatalog.Deliver.Label);
    }

    [Fact]
    public void AllStages_HaveUniqueNames()
    {
        var stages = StageCatalog.All;
        Assert.Equal(6, stages.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void AllStages_AreInPipelineOrder()
    {
        var stageNames = StageCatalog.All.Select(stage => stage.Name).ToArray();

        Assert.Equal<string>(["triage", "plan", "implement", "review", "docs", "deliver"], stageNames);
    }
}
