using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class PipelineDefinitionTests
{
    [Fact]
    public void DefaultDefinition_UsesStandardCyberpilotIdentity()
    {
        var definition = DefaultPipelineDefinitionProvider.Definition;

        Assert.Equal("cyberpilot-default", definition.Name);
        Assert.Equal("1.0", definition.Version.Value);
        Assert.Equal("standard", definition.PolicyProfile.Name);
        Assert.Equal(PolicyStrictness.Standard, definition.PolicyProfile.Strictness);
    }

    [Fact]
    public void DefaultDefinition_StagesMatchStageCatalog()
    {
        var definitionStages = DefaultPipelineDefinitionProvider.Definition.Stages
            .Select(stage => stage.Stage)
            .ToArray();

        Assert.Equal(StageCatalog.All, definitionStages);
    }

    [Fact]
    public void DefaultDefinition_StagesPreserveCurrentPipelineOrder()
    {
        var stageNames = DefaultPipelineDefinitionProvider.Definition.Stages
            .Select(stage => stage.Stage.Name)
            .ToArray();

        Assert.Equal<string>(["triage", "plan", "implement", "review", "docs", "deliver"], stageNames);
    }

    [Fact]
    public void DefaultDefinition_StagesUseCurrentPromptFilesAndLabels()
    {
        var stageData = DefaultPipelineDefinitionProvider.Definition.Stages
            .Select(stage => new
            {
                stage.Stage.Name,
                stage.Stage.PromptFile,
                stage.Stage.Label,
            })
            .ToArray();

        Assert.Collection(
            stageData,
            stage =>
            {
                Assert.Equal("triage", stage.Name);
                Assert.Equal("triage.agent.md", stage.PromptFile);
                Assert.Equal("sdk/triage", stage.Label);
            },
            stage =>
            {
                Assert.Equal("plan", stage.Name);
                Assert.Equal("plan.agent.md", stage.PromptFile);
                Assert.Equal("sdk/planning", stage.Label);
            },
            stage =>
            {
                Assert.Equal("implement", stage.Name);
                Assert.Equal("implement.agent.md", stage.PromptFile);
                Assert.Equal("sdk/implementing", stage.Label);
            },
            stage =>
            {
                Assert.Equal("review", stage.Name);
                Assert.Equal("pipeline-review.agent.md", stage.PromptFile);
                Assert.Equal("sdk/review", stage.Label);
            },
            stage =>
            {
                Assert.Equal("docs", stage.Name);
                Assert.Equal("docs.agent.md", stage.PromptFile);
                Assert.Equal("sdk/docs", stage.Label);
            },
            stage =>
            {
                Assert.Equal("deliver", stage.Name);
                Assert.Equal("deliver.agent.md", stage.PromptFile);
                Assert.Equal("sdk/delivering", stage.Label);
            });
    }

    [Fact]
    public void DefaultDefinition_DeclaresInitialTransitionMap()
    {
        var transitions = DefaultPipelineDefinitionProvider.Definition.Transitions;

        Assert.Contains(transitions, transition => transition is { FromStage: "review", ToStage: "implement", Condition: "changes_requested" });
        Assert.Contains(transitions, transition => transition is { FromStage: "review", ToStage: "docs", Condition: "approved" });
    }

    [Fact]
    public void PipelineDefinitionSelector_DefaultOptions_SelectsDefaultDefinition()
    {
        var options = new Cyberpilot.Options.CyberpilotOptions(1, Directory.GetCurrentDirectory(), "owner/repo", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);

        var selected = PipelineDefinitionSelector.TrySelect(options, out var definition, out var error);

        Assert.True(selected);
        Assert.Same(DefaultPipelineDefinitionProvider.Definition, definition);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("other-definition", "1.0", "standard", "Unsupported pipeline definition")]
    [InlineData("cyberpilot-default", "2.0", "standard", "Unsupported pipeline definition version")]
    [InlineData("cyberpilot-default", "1.0", "strict", "Unsupported policy profile")]
    public void PipelineDefinitionSelector_UnsupportedSelection_ReturnsError(string definitionName, string definitionVersion, string policyProfile, string expectedError)
    {
        var options = new Cyberpilot.Options.CyberpilotOptions(
            1,
            Directory.GetCurrentDirectory(),
            "owner/repo",
            "test-model",
            false,
            false,
            false,
            false,
            TimeSpan.FromMinutes(10),
            true,
            false,
            null,
            null,
            false,
            PipelineDefinitionName: definitionName,
            PipelineDefinitionVersion: definitionVersion,
            PolicyProfileName: policyProfile);

        var selected = PipelineDefinitionSelector.TrySelect(options, out var definition, out var error);

        Assert.False(selected);
        Assert.Null(definition);
        Assert.Contains(expectedError, error);
    }
}
