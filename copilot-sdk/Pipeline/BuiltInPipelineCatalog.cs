namespace Cyberpilot.Pipeline;

/// <summary>
/// Describes a selectable built-in Cyberpilot pipeline definition.
/// </summary>
/// <param name="Name">The pipeline definition name.</param>
/// <param name="Version">The pipeline definition version.</param>
/// <param name="Description">A short operator-facing description.</param>
public sealed record PipelineDefinitionMetadata(string Name, string Version, string Description);

/// <summary>
/// Describes a selectable built-in Cyberpilot policy profile.
/// </summary>
/// <param name="Name">The policy profile name.</param>
/// <param name="Description">A short operator-facing description.</param>
public sealed record PolicyProfileMetadata(string Name, string Description);

/// <summary>
/// Provides public metadata for built-in Cyberpilot pipeline definitions and policy profiles.
/// </summary>
public static class BuiltInPipelineCatalog
{
    /// <summary>The bugfix built-in pipeline definition name.</summary>
    public const string BugfixDefinitionName = "bugfix";

    /// <summary>The docs-only built-in pipeline definition name.</summary>
    public const string DocsOnlyDefinitionName = "docs-only";

    /// <summary>Gets the built-in pipeline definitions that operators can select.</summary>
    public static IReadOnlyList<PipelineDefinitionMetadata> Definitions { get; } =
    [
        new(PipelineDefinitionDefaults.DefinitionName, PipelineDefinitionDefaults.DefinitionVersion, "Full issue-to-PR SDLC"),
        new(BugfixDefinitionName, PipelineDefinitionDefaults.DefinitionVersion, "Plan, implement, review, and deliver a focused fix"),
        new(DocsOnlyDefinitionName, PipelineDefinitionDefaults.DefinitionVersion, "Documentation and landing report only"),
    ];

    /// <summary>Gets the built-in policy profiles that operators can select.</summary>
    public static IReadOnlyList<PolicyProfileMetadata> PolicyProfiles { get; } =
    [
        new("lenient", "Advisory checks with minimal blocking"),
        new(PipelineDefinitionDefaults.PolicyProfileName, "Balanced default gates and diagnostics"),
        new("strict", "More conservative validation and gate handling"),
        new("security-critical", "Security-sensitive policy posture"),
    ];

    /// <summary>Gets the comma-separated built-in pipeline definition names.</summary>
    public static string AvailableDefinitionNames => string.Join(", ", Definitions.Select(definition => definition.Name));

    /// <summary>Gets the comma-separated built-in policy profile names.</summary>
    public static string AvailablePolicyProfileNames => string.Join(", ", PolicyProfiles.Select(profile => profile.Name));

    /// <summary>Attempts to resolve a built-in pipeline definition by name.</summary>
    /// <param name="name">The requested pipeline definition name.</param>
    /// <param name="definition">The matching pipeline definition metadata when found.</param>
    /// <returns><see langword="true" /> when the definition exists; otherwise <see langword="false" />.</returns>
    public static bool TryGetDefinition(string? name, out PipelineDefinitionMetadata? definition)
    {
        definition = Definitions.FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return definition is not null;
    }

    /// <summary>Attempts to resolve a built-in policy profile by name.</summary>
    /// <param name="name">The requested policy profile name.</param>
    /// <param name="profile">The matching policy profile metadata when found.</param>
    /// <returns><see langword="true" /> when the profile exists; otherwise <see langword="false" />.</returns>
    public static bool TryGetPolicyProfile(string? name, out PolicyProfileMetadata? profile)
    {
        profile = PolicyProfiles.FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return profile is not null;
    }
}