using Cyberpilot.Copilot;
using Cyberpilot.GitHub;

namespace Cyberpilot.Pipeline;

internal static class BuiltInPipelineGates
{
    public const string ModelAvailable = "model-available";
    public const string RequiredLabels = "required-labels";

    public static IReadOnlyDictionary<string, IPipelineGate> Create(IModelAvailabilityChecker modelChecker, ISdkLabelService labels)
        => new Dictionary<string, IPipelineGate>(StringComparer.OrdinalIgnoreCase)
        {
            [ModelAvailable] = new ModelAvailabilityGate(modelChecker),
            [RequiredLabels] = new RequiredLabelsGate(labels),
        };
}
