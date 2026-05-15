namespace Cyberpilot.Pipeline;

internal static class PipelineDefinitionValidator
{
    public static IReadOnlyList<string> Validate(PipelineDefinition definition)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("Pipeline definition name is required.");

        if (string.IsNullOrWhiteSpace(definition.Version.Value))
            errors.Add("Pipeline definition version is required.");

        if (string.IsNullOrWhiteSpace(definition.PolicyProfile.Name))
            errors.Add("Pipeline policy profile name is required.");

        if (definition.Stages.Count == 0)
        {
            errors.Add("Pipeline definition must declare at least one stage.");
            return errors;
        }

        var stageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in definition.Stages)
            ValidateStage(stage, stageNames, errors);

        foreach (var transition in definition.Transitions)
            ValidateTransition(transition, stageNames, errors);

        return errors;
    }

    private static void ValidateStage(PipelineStageDefinition stage, HashSet<string> stageNames, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(stage.Stage.Name))
        {
            errors.Add("Pipeline stage name is required.");
            return;
        }

        if (!stageNames.Add(stage.Stage.Name))
            errors.Add($"Pipeline stage '{stage.Stage.Name}' is declared more than once.");

        if (string.IsNullOrWhiteSpace(stage.Stage.PromptFile))
            errors.Add($"Pipeline stage '{stage.Stage.Name}' must declare a prompt file.");

        if (string.IsNullOrWhiteSpace(stage.Stage.Label))
            errors.Add($"Pipeline stage '{stage.Stage.Name}' must declare a label.");

        if (string.IsNullOrWhiteSpace(stage.Contract.Version))
            errors.Add($"Pipeline stage '{stage.Stage.Name}' must declare a contract version.");
    }

    private static void ValidateTransition(StageTransition transition, HashSet<string> stageNames, List<string> errors)
    {
        if (!stageNames.Contains(transition.FromStage))
            errors.Add($"Pipeline transition starts from unknown stage '{transition.FromStage}'.");

        if (!stageNames.Contains(transition.ToStage))
            errors.Add($"Pipeline transition targets unknown stage '{transition.ToStage}'.");

        if (string.IsNullOrWhiteSpace(transition.Condition))
            errors.Add($"Pipeline transition from '{transition.FromStage}' to '{transition.ToStage}' must declare a condition.");
    }
}