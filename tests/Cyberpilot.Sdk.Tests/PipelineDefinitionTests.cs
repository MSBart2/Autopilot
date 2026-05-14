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

    [Theory]
    [InlineData("review", "changes_requested", "implement")]
    [InlineData("review", "approved", "docs")]
    public void DefaultDefinition_TransitionTarget_ResolvesDeclaredTransition(string fromStage, string condition, string expectedTargetStage)
    {
        var target = DefaultPipelineDefinitionProvider.Definition.TransitionTarget(fromStage, condition);

        Assert.Equal(expectedTargetStage, target.Name);
    }

    [Fact]
    public void DefaultDefinition_TransitionTarget_ThrowsForMissingTransition()
    {
        Assert.Throws<MissingPipelineTransitionException>(() =>
            DefaultPipelineDefinitionProvider.Definition.TransitionTarget("docs", "changes_requested"));
    }

    [Fact]
    public void PipelineStartResolver_UsesSelectedDefinitionOrder()
    {
        var definition = CreateCustomDefinition(
            Stage("alpha", "sdk/alpha"),
            Stage("review", "sdk/custom-review"),
            Stage("omega", "sdk/omega"));

        var start = PipelineStartResolver.Resolve("review", definition);

        Assert.Equal(1, start.Index);
        Assert.True(start.IsResume);
        Assert.Equal("review", start.Stage.Name);
        Assert.Equal("sdk/custom-review", start.Stage.Label);
    }

    [Fact]
    public void PipelineDefinitionStageLookup_ShouldRun_UsesSelectedDefinitionOrder()
    {
        var definition = CreateCustomDefinition(
            Stage("alpha", "sdk/alpha"),
            Stage("review", "sdk/custom-review"),
            Stage("omega", "sdk/omega"));
        var start = PipelineStartResolver.Resolve("review", definition);

        Assert.False(definition.ShouldRun(start, definition.Stage("alpha")));
        Assert.True(definition.ShouldRun(start, definition.Stage("review")));
        Assert.True(definition.ShouldRun(start, definition.Stage("omega")));
    }

    [Fact]
    public void PipelineDefinitionStageLookup_UnknownStage_Throws()
    {
        var definition = CreateCustomDefinition(Stage("alpha", "sdk/alpha"));

        Assert.Throws<UnknownPipelineStageException>(() => definition.Stage("missing"));
    }

    [Fact]
    public void PipelineDefinitionSelector_DefaultOptions_SelectsDefaultDefinition()
    {
        var options = new Cyberpilot.Options.CyberpilotOptions(1, Directory.GetCurrentDirectory(), "owner/repo", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);

        var selected = PipelineDefinitionSelector.TrySelect(options, out var definition, out var error);

        Assert.True(selected);
        Assert.Equal(DefaultPipelineDefinitionProvider.Definition.Name, definition!.Name);
        Assert.Equal("standard", definition.PolicyProfile.Name);
        Assert.Null(error);
    }

    [Fact]
    public void BuiltInPipelineDefinitions_ListsDefaultDefinition()
    {
        var found = BuiltInPipelineDefinitions.TryGet(PipelineDefinitionDefaults.DefinitionName, out var definition);

        Assert.True(found);
        Assert.Same(DefaultPipelineDefinitionProvider.Definition, definition);
        Assert.Contains(PipelineDefinitionDefaults.DefinitionName, BuiltInPipelineDefinitions.AvailableNames);
    }

    [Fact]
    public void BuiltInPipelineDefinitions_ListsDocsOnlyDefinition()
    {
        var found = BuiltInPipelineDefinitions.TryGet("docs-only", out var definition);

        Assert.True(found);
        Assert.Equal("docs-only", definition!.Name);
        Assert.Equal<string>(["docs", "deliver"], definition.Stages.Select(stage => stage.Stage.Name).ToArray());
        Assert.Contains(definition.Transitions, transition => transition is { FromStage: "docs", ToStage: "deliver", Condition: "GO" });
        Assert.Contains("docs-only", BuiltInPipelineDefinitions.AvailableNames);
    }

    [Fact]
    public void PipelineDefinitionSelector_DocsOnlyDefinition_SelectsVariant()
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
            PipelineDefinitionName: "docs-only");

        var selected = PipelineDefinitionSelector.TrySelect(options, out var definition, out var error);

        Assert.True(selected);
        Assert.Null(error);
        Assert.Equal("docs-only", definition!.Name);
        Assert.Equal<string>(["docs", "deliver"], definition.Stages.Select(stage => stage.Stage.Name).ToArray());
        Assert.Equal("standard", definition.PolicyProfile.Name);
    }

    [Theory]
    [InlineData("lenient", "Lenient")]
    [InlineData("standard", "Standard")]
    [InlineData("strict", "Strict")]
    [InlineData("security-critical", "SecurityCritical")]
    public void PipelineDefinitionSelector_BuiltInPolicyProfile_SelectsProfile(string policyProfileName, string expectedStrictness)
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
            PolicyProfileName: policyProfileName);

        var selected = PipelineDefinitionSelector.TrySelect(options, out var definition, out var error);

        Assert.True(selected);
        Assert.Null(error);
        Assert.Equal(policyProfileName, definition!.PolicyProfile.Name);
        Assert.Equal(expectedStrictness, definition.PolicyProfile.Strictness.ToString());
        Assert.Equal(DefaultPipelineDefinitionProvider.Definition.Stages, definition.Stages);
    }

    [Fact]
    public void PipelineDefinitionValidator_DefaultDefinition_HasNoErrors()
    {
        var errors = PipelineDefinitionValidator.Validate(DefaultPipelineDefinitionProvider.Definition);

        Assert.Empty(errors);
    }

    [Fact]
    public void PipelineDefinitionValidator_InvalidDefinition_ReturnsActionableErrors()
    {
        var duplicateStage = new StageDefinition("TRIAGE", "triage", string.Empty, string.Empty);
        var definition = new PipelineDefinition(
            string.Empty,
            new PipelineDefinitionVersion(string.Empty),
            new PolicyProfile(string.Empty, PolicyStrictness.Standard),
            [
                new PipelineStageDefinition(duplicateStage, new StageContract(string.Empty, []), []),
                new PipelineStageDefinition(duplicateStage, new StageContract("1.0", []), []),
            ],
            [new StageTransition("triage", "missing", string.Empty)]);

        var errors = PipelineDefinitionValidator.Validate(definition);

        Assert.Contains("Pipeline definition name is required.", errors);
        Assert.Contains("Pipeline definition version is required.", errors);
        Assert.Contains("Pipeline policy profile name is required.", errors);
        Assert.Contains("Pipeline stage 'triage' is declared more than once.", errors);
        Assert.Contains("Pipeline stage 'triage' must declare a prompt file.", errors);
        Assert.Contains("Pipeline stage 'triage' must declare a label.", errors);
        Assert.Contains("Pipeline stage 'triage' must declare a contract version.", errors);
        Assert.Contains("Pipeline transition targets unknown stage 'missing'.", errors);
        Assert.Contains("Pipeline transition from 'triage' to 'missing' must declare a condition.", errors);
    }

    [Theory]
    [InlineData("other-definition", "1.0", "standard", "Unsupported pipeline definition")]
    [InlineData("docs-only", "2.0", "standard", "Unsupported pipeline definition version")]
    [InlineData("cyberpilot-default", "2.0", "standard", "Unsupported pipeline definition version")]
    [InlineData("cyberpilot-default", "1.0", "unknown-profile", "Unsupported policy profile")]
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

    private static PipelineDefinition CreateCustomDefinition(params StageDefinition[] stages)
        => new(
            "custom",
            new PipelineDefinitionVersion("1.0"),
            new PolicyProfile("standard", PolicyStrictness.Standard),
            stages.Select(stage => new PipelineStageDefinition(stage, new StageContract("1.0", []), [])).ToArray(),
            []);

    private static StageDefinition Stage(string name, string label)
        => new(name.ToUpperInvariant(), name, $"{name}.agent.md", label);
}
