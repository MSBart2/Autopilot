namespace Cyberpilot.Pipeline;

internal static class PipelineDefinitionTransitionLookup
{
    public static StageTransition? Transition(this PipelineDefinition definition, string fromStage, string condition)
    {
        return definition.Transitions.FirstOrDefault(transition =>
            transition.FromStage.Equals(fromStage, StringComparison.OrdinalIgnoreCase)
            && transition.Condition.Equals(condition, StringComparison.OrdinalIgnoreCase));
    }

    public static StageDefinition TransitionTarget(this PipelineDefinition definition, string fromStage, string condition)
    {
        var transition = definition.Transition(fromStage, condition)
            ?? throw new MissingPipelineTransitionException(fromStage, condition);
        return definition.Stage(transition.ToStage);
    }
}

internal sealed class MissingPipelineTransitionException(string fromStage, string condition)
    : Exception($"Pipeline definition is missing transition from '{fromStage}' for condition '{condition}'.")
{
}