using System.Text.Json;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Models;
using Microsoft.Extensions.Options;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Reads and writes operator-managed pipeline definition JSON used by the SDK runner.
/// </summary>
public interface IPipelineDefinitionAdminStore
{
    /// <summary>Gets the absolute path to the editable pipeline definition file.</summary>
    string DefinitionFilePath { get; }

    /// <summary>Reads the editable pipeline configuration.</summary>
    Task<PipelineDefinitionAdminFile> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves an editable pipeline definition.</summary>
    Task SaveDefinitionAsync(PipelineAdminDefinitionEditViewModel model, CancellationToken cancellationToken = default);

    /// <summary>Deletes an editable pipeline definition.</summary>
    Task DeleteDefinitionAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Saves an editable policy profile.</summary>
    Task SavePolicyAsync(PipelineAdminPolicyEditViewModel model, CancellationToken cancellationToken = default);

    /// <summary>Deletes an editable policy profile.</summary>
    Task DeletePolicyAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Gets all launchable pipeline definition options.</summary>
    Task<IReadOnlyList<PipelineDefinitionOptionViewModel>> GetDefinitionOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets all launchable policy profile options.</summary>
    Task<IReadOnlyList<PipelinePolicyOptionViewModel>> GetPolicyOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Attempts to find a configured editable definition.</summary>
    Task<PipelineAdminDefinition?> FindDefinitionAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Attempts to find a configured editable policy profile.</summary>
    Task<PipelineAdminPolicyProfile?> FindPolicyAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// File-backed implementation of <see cref="IPipelineDefinitionAdminStore"/>.
/// </summary>
public sealed class PipelineDefinitionAdminStore : IPipelineDefinitionAdminStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IWebHostEnvironment environment;
    private readonly CyberpilotWebOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineDefinitionAdminStore"/> class.
    /// </summary>
    /// <param name="environment">The web hosting environment.</param>
    /// <param name="options">Cyberpilot web options.</param>
    public PipelineDefinitionAdminStore(IWebHostEnvironment environment, IOptions<CyberpilotWebOptions> options)
    {
        this.environment = environment;
        this.options = options.Value;
    }

    /// <inheritdoc />
    public string DefinitionFilePath => ResolveDefinitionFilePath();

    /// <inheritdoc />
    public async Task<PipelineDefinitionAdminFile> ReadAsync(CancellationToken cancellationToken = default)
    {
        var path = DefinitionFilePath;
        if (!File.Exists(path))
        {
            return PipelineDefinitionAdminFile.Empty;
        }

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<PipelineDefinitionAdminFile>(stream, SerializerOptions, cancellationToken);
        return file ?? PipelineDefinitionAdminFile.Empty;
    }

    /// <inheritdoc />
    public async Task SaveDefinitionAsync(PipelineAdminDefinitionEditViewModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var file = await ReadAsync(cancellationToken);
        var definition = ToDefinition(model, file.PolicyProfiles);
        var definitions = file.Definitions
            .Where(item => !item.Name.Equals(model.OriginalName ?? model.Name, StringComparison.OrdinalIgnoreCase))
            .Append(definition)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await WriteAsync(file with { Definitions = definitions }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteDefinitionAsync(string name, CancellationToken cancellationToken = default)
    {
        var file = await ReadAsync(cancellationToken);
        await WriteAsync(file with
        {
            Definitions = file.Definitions
                .Where(item => !item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SavePolicyAsync(PipelineAdminPolicyEditViewModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var policy = new PipelineAdminPolicyProfile(model.Name.Trim(), model.Strictness.Trim(), model.Description.Trim());
        var file = await ReadAsync(cancellationToken);
        var policies = file.PolicyProfiles
            .Where(item => !item.Name.Equals(model.OriginalName ?? model.Name, StringComparison.OrdinalIgnoreCase))
            .Append(policy)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await WriteAsync(file with { PolicyProfiles = policies }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeletePolicyAsync(string name, CancellationToken cancellationToken = default)
    {
        var file = await ReadAsync(cancellationToken);
        await WriteAsync(file with
        {
            PolicyProfiles = file.PolicyProfiles
                .Where(item => !item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PipelineDefinitionOptionViewModel>> GetDefinitionOptionsAsync(CancellationToken cancellationToken = default)
    {
        var file = await ReadAsync(cancellationToken);
        return BuiltInPipelineCatalog.Definitions
            .Select(definition => new PipelineDefinitionOptionViewModel(definition.Name, definition.Version, definition.Description, true))
            .Concat(file.Definitions.Select(definition => new PipelineDefinitionOptionViewModel(definition.Name, definition.Version, $"Custom pipeline ({definition.Stages.Count} stages)", false)))
            .GroupBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PipelinePolicyOptionViewModel>> GetPolicyOptionsAsync(CancellationToken cancellationToken = default)
    {
        var file = await ReadAsync(cancellationToken);
        return BuiltInPipelineCatalog.PolicyProfiles
            .Select(profile => new PipelinePolicyOptionViewModel(profile.Name, profile.Description, true))
            .Concat(file.PolicyProfiles.Select(profile => new PipelinePolicyOptionViewModel(profile.Name, profile.Description, false)))
            .GroupBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<PipelineAdminDefinition?> FindDefinitionAsync(string name, CancellationToken cancellationToken = default)
    {
        var file = await ReadAsync(cancellationToken);
        return file.Definitions.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<PipelineAdminPolicyProfile?> FindPolicyAsync(string name, CancellationToken cancellationToken = default)
    {
        var file = await ReadAsync(cancellationToken);
        return file.PolicyProfiles.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task WriteAsync(PipelineDefinitionAdminFile file, CancellationToken cancellationToken)
    {
        var path = DefinitionFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, file, SerializerOptions, cancellationToken);
    }

    private string ResolveDefinitionFilePath()
    {
        var configuredPath = options.PipelineDefinitionFilePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = Path.Combine("App_Data", "pipeline-definitions.json");
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
    }

    private static PipelineAdminDefinition ToDefinition(PipelineAdminDefinitionEditViewModel model, IReadOnlyList<PipelineAdminPolicyProfile> policies)
    {
        var stages = model.Stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.Name))
            .Select(ToStage)
            .ToArray();

        if (stages.Length == 0)
        {
            throw new InvalidOperationException("At least one stage is required.");
        }

        var policy = policies.FirstOrDefault(item => item.Name.Equals(model.PolicyProfileName, StringComparison.OrdinalIgnoreCase));
        var strictness = policy?.Strictness ?? "Standard";
        var transitions = ParseTransitions(model.TransitionsText, stages);

        return new PipelineAdminDefinition(
            model.Name.Trim(),
            model.Version.Trim(),
            new PipelineAdminDefinitionPolicy(model.PolicyProfileName.Trim(), strictness),
            stages,
            transitions);
    }

    private static PipelineAdminStage ToStage(PipelineAdminStageInputModel input)
    {
        return new PipelineAdminStage(
            input.DisplayName.Trim(),
            input.Name.Trim(),
            input.PromptFile.Trim(),
            input.Label.Trim(),
            new PipelineAdminStageContract(input.ContractVersion.Trim(), SplitCsv(input.RequiredArtifactsText)),
            ParseGates(input.GatesText));
    }

    private static IReadOnlyList<PipelineAdminGate> ParseGates(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            .Select(parts => new PipelineAdminGate(
                parts[0],
                parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : nameof(GateTiming.AfterStage),
                parts.Length <= 2 || bool.TryParse(parts[2], out var isBlocking) && isBlocking))
            .ToArray();
    }

    private static IReadOnlyList<PipelineAdminTransition> ParseTransitions(string? text, IReadOnlyList<PipelineAdminStage> stages)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return stages.Zip(stages.Skip(1), (from, to) => new PipelineAdminTransition(from.Name, to.Name, "go")).ToArray();
        }

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => new PipelineAdminTransition(parts[0], parts[1], parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : "go"))
            .ToArray();
    }

    private static IReadOnlyList<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>
/// Root JSON document for editable pipeline definitions.
/// </summary>
/// <param name="Definitions">The custom pipeline definitions.</param>
/// <param name="PolicyProfiles">The custom policy profiles.</param>
public sealed record PipelineDefinitionAdminFile(
    IReadOnlyList<PipelineAdminDefinition> Definitions,
    IReadOnlyList<PipelineAdminPolicyProfile> PolicyProfiles)
{
    /// <summary>Gets an empty editable definition file.</summary>
    public static PipelineDefinitionAdminFile Empty { get; } = new([], []);
}

/// <summary>One editable custom pipeline definition.</summary>
public sealed record PipelineAdminDefinition(string Name, string Version, PipelineAdminDefinitionPolicy PolicyProfile, IReadOnlyList<PipelineAdminStage> Stages, IReadOnlyList<PipelineAdminTransition> Transitions);

/// <summary>One editable custom policy profile reference on a definition.</summary>
public sealed record PipelineAdminDefinitionPolicy(string Name, string Strictness);

/// <summary>One editable stage in a custom pipeline.</summary>
public sealed record PipelineAdminStage(string DisplayName, string Name, string PromptFile, string Label, PipelineAdminStageContract Contract, IReadOnlyList<PipelineAdminGate> Gates);

/// <summary>One editable stage contract.</summary>
public sealed record PipelineAdminStageContract(string Version, IReadOnlyList<string> RequiredArtifacts);

/// <summary>One editable stage gate.</summary>
public sealed record PipelineAdminGate(string Name, string Timing, bool IsBlocking);

/// <summary>One editable transition between stages.</summary>
public sealed record PipelineAdminTransition(string FromStage, string ToStage, string Condition);

/// <summary>One editable custom policy profile.</summary>
public sealed record PipelineAdminPolicyProfile(string Name, string Strictness, string Description);
