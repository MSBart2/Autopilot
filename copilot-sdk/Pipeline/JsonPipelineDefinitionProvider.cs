using System.Text.Json;

namespace Cyberpilot.Pipeline;

internal sealed class JsonPipelineDefinitionProvider : IPipelineDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, PipelineDefinition> definitions;

    private JsonPipelineDefinitionProvider(IReadOnlyDictionary<string, PipelineDefinition> definitions)
    {
        this.definitions = definitions;
    }

    public string AvailableNames => string.Join(", ", definitions.Keys.Order(StringComparer.OrdinalIgnoreCase));

    public static bool TryLoad(string path, out JsonPipelineDefinitionProvider? provider, out string? error)
    {
        provider = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Pipeline definition file path is required.";
            return false;
        }

        if (!File.Exists(path))
        {
            error = $"Pipeline definition file '{path}' was not found.";
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<PipelineDefinitionFile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (file?.Definitions is null || file.Definitions.Count == 0)
            {
                error = $"Pipeline definition file '{path}' does not contain any definitions.";
                return false;
            }

            var loadedDefinitions = new Dictionary<string, PipelineDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var fileDefinition in file.Definitions)
            {
                var definition = CreateDefinition(fileDefinition);
                if (loadedDefinitions.ContainsKey(definition.Name))
                {
                    error = $"Pipeline definition file '{path}' declares '{definition.Name}' more than once.";
                    return false;
                }

                loadedDefinitions[definition.Name] = definition;
            }

            provider = new JsonPipelineDefinitionProvider(loadedDefinitions);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            error = $"Pipeline definition file '{path}' is invalid: {ex.Message}";
            return false;
        }
    }

    public bool TryGet(string name, out PipelineDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            definition = null;
            return false;
        }

        return definitions.TryGetValue(name, out definition);
    }

    private static PipelineDefinition CreateDefinition(FilePipelineDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new InvalidOperationException("A pipeline definition name is required.");
        }

        var policyProfile = definition.PolicyProfile is null
            ? new PolicyProfile(PipelineDefinitionDefaults.PolicyProfileName, PolicyStrictness.Standard)
            : new PolicyProfile(
                Required(definition.PolicyProfile.Name, $"Pipeline definition '{definition.Name}' policy profile name is required."),
                ParseStrictness(definition.PolicyProfile.Strictness));

        return new PipelineDefinition(
            definition.Name,
            new PipelineDefinitionVersion(Required(definition.Version, $"Pipeline definition '{definition.Name}' version is required.")),
            policyProfile,
            definition.Stages.Select(stage => CreateStage(definition.Name, stage)).ToArray(),
            definition.Transitions.Select(CreateTransition).ToArray());
    }

    private static PipelineStageDefinition CreateStage(string definitionName, FilePipelineStage stage)
    {
        var stageName = Required(stage.Name, $"Pipeline definition '{definitionName}' contains a stage without a name.");
        var contract = stage.Contract ?? new FileStageContract();
        return new PipelineStageDefinition(
            new StageDefinition(
                Required(stage.DisplayName, $"Pipeline stage '{stageName}' displayName is required."),
                stageName,
                Required(stage.PromptFile, $"Pipeline stage '{stageName}' promptFile is required."),
                Required(stage.Label, $"Pipeline stage '{stageName}' label is required.")),
            new StageContract(Required(contract.Version, $"Pipeline stage '{stageName}' contract version is required."), contract.RequiredArtifacts),
            stage.Gates.Select(gate => new GateDefinition(
                Required(gate.Name, $"Pipeline stage '{stageName}' gate name is required."),
                ParseGateTiming(gate.Timing),
                gate.IsBlocking)).ToArray());
    }

    private static StageTransition CreateTransition(FileStageTransition transition)
        => new(
            Required(transition.FromStage, "Pipeline transition fromStage is required."),
            Required(transition.ToStage, "Pipeline transition toStage is required."),
            Required(transition.Condition, "Pipeline transition condition is required."));

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        return value;
    }

    private static PolicyStrictness ParseStrictness(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PolicyStrictness.Standard;
        }

        return Enum.TryParse<PolicyStrictness>(value, ignoreCase: true, out var strictness)
            ? strictness
            : throw new InvalidOperationException($"Unknown policy strictness '{value}'.");
    }

    private static GateTiming ParseGateTiming(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GateTiming.AfterStage;
        }

        return Enum.TryParse<GateTiming>(value, ignoreCase: true, out var timing)
            ? timing
            : throw new InvalidOperationException($"Unknown gate timing '{value}'.");
    }

    private sealed record PipelineDefinitionFile(IReadOnlyList<FilePipelineDefinition> Definitions);

    private sealed record FilePipelineDefinition(
        string? Name,
        string? Version,
        FilePolicyProfile? PolicyProfile,
        IReadOnlyList<FilePipelineStage> Stages,
        IReadOnlyList<FileStageTransition> Transitions)
    {
        public IReadOnlyList<FilePipelineStage> Stages { get; init; } = Stages ?? [];

        public IReadOnlyList<FileStageTransition> Transitions { get; init; } = Transitions ?? [];
    }

    private sealed record FilePolicyProfile(string? Name, string? Strictness);

    private sealed record FilePipelineStage(
        string? DisplayName,
        string? Name,
        string? PromptFile,
        string? Label,
        FileStageContract? Contract,
        IReadOnlyList<FileGateDefinition> Gates)
    {
        public IReadOnlyList<FileGateDefinition> Gates { get; init; } = Gates ?? [];
    }

    private sealed record FileStageContract(string? Version = null, IReadOnlyList<string>? RequiredArtifacts = null)
    {
        public IReadOnlyList<string> RequiredArtifacts { get; init; } = RequiredArtifacts ?? [];
    }

    private sealed record FileGateDefinition(string? Name, string? Timing, bool IsBlocking);

    private sealed record FileStageTransition(string? FromStage, string? ToStage, string? Condition);
}