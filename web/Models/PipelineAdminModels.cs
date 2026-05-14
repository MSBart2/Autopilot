using System.ComponentModel.DataAnnotations;
using Cyberpilot.Pipeline;

namespace Cyberpilot.Web.Models;

/// <summary>
/// Displays configured editable pipeline definitions and policy profiles.
/// </summary>
/// <param name="Definitions">The editable pipeline definitions.</param>
/// <param name="PolicyProfiles">The editable policy profiles.</param>
/// <param name="DefinitionFilePath">The backing JSON definition file path.</param>
public sealed record PipelineAdminIndexViewModel(
    IReadOnlyList<PipelineAdminDefinitionSummaryViewModel> Definitions,
    IReadOnlyList<PipelineAdminPolicySummaryViewModel> PolicyProfiles,
    string DefinitionFilePath);

/// <summary>
/// Summarizes one editable pipeline definition.
/// </summary>
/// <param name="Name">The pipeline definition name.</param>
/// <param name="Version">The definition version.</param>
/// <param name="PolicyProfileName">The associated policy profile name.</param>
/// <param name="StageCount">The number of stages in the definition.</param>
public sealed record PipelineAdminDefinitionSummaryViewModel(string Name, string Version, string PolicyProfileName, int StageCount);

/// <summary>
/// Summarizes one editable policy profile.
/// </summary>
/// <param name="Name">The policy profile name.</param>
/// <param name="Strictness">The policy strictness.</param>
/// <param name="Description">The operator-facing policy description.</param>
public sealed record PipelineAdminPolicySummaryViewModel(string Name, string Strictness, string Description);

/// <summary>
/// Describes one pipeline definition option available in launch forms.
/// </summary>
/// <param name="Name">The pipeline definition name.</param>
/// <param name="Version">The pipeline definition version.</param>
/// <param name="Description">The display description.</param>
/// <param name="IsBuiltIn">Whether this definition is built into the SDK.</param>
public sealed record PipelineDefinitionOptionViewModel(string Name, string Version, string Description, bool IsBuiltIn);

/// <summary>
/// Describes one policy profile option available in launch forms.
/// </summary>
/// <param name="Name">The policy profile name.</param>
/// <param name="Description">The display description.</param>
/// <param name="IsBuiltIn">Whether this profile is built into the SDK.</param>
public sealed record PipelinePolicyOptionViewModel(string Name, string Description, bool IsBuiltIn);

/// <summary>
/// Captures editable pipeline definition metadata, stages, and transitions.
/// </summary>
public sealed class PipelineAdminDefinitionEditViewModel
{
    /// <summary>Gets or sets the original name when editing an existing definition.</summary>
    [StringLength(80)]
    public string? OriginalName { get; set; }

    /// <summary>Gets or sets the pipeline definition name.</summary>
    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the pipeline definition version.</summary>
    [Required]
    [StringLength(40)]
    public string Version { get; set; } = PipelineDefinitionDefaults.DefinitionVersion;

    /// <summary>Gets or sets the selected policy profile name.</summary>
    [Required]
    [StringLength(80)]
    public string PolicyProfileName { get; set; } = PipelineDefinitionDefaults.PolicyProfileName;

    /// <summary>Gets or sets the editable policy profile options.</summary>
    public IReadOnlyList<PipelineAdminPolicySummaryViewModel> PolicyProfiles { get; set; } = [];

    /// <summary>Gets or sets the stage rows in execution order.</summary>
    [MinLength(1, ErrorMessage = "At least one stage is required.")]
    public List<PipelineAdminStageInputModel> Stages { get; set; } = [];

    /// <summary>Gets or sets newline-separated transitions in the form: from|to|condition.</summary>
    [StringLength(4000)]
    public string? TransitionsText { get; set; }
}

/// <summary>
/// Captures one editable pipeline stage row.
/// </summary>
public sealed class PipelineAdminStageInputModel
{
    /// <summary>Gets or sets the stage display name.</summary>
    [Required]
    [StringLength(80)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable stage key.</summary>
    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the agent prompt file name.</summary>
    [Required]
    [StringLength(200)]
    public string PromptFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the stage label value.</summary>
    [Required]
    [StringLength(120)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the stage contract version.</summary>
    [Required]
    [StringLength(40)]
    public string ContractVersion { get; set; } = PipelineDefinitionDefaults.ContractVersion;

    /// <summary>Gets or sets comma-separated required artifact names.</summary>
    [StringLength(1000)]
    public string? RequiredArtifactsText { get; set; }

    /// <summary>Gets or sets newline-separated gates in the form: name|timing|blocking.</summary>
    [StringLength(2000)]
    public string? GatesText { get; set; }
}

/// <summary>
/// Captures editable policy profile values.
/// </summary>
public sealed class PipelineAdminPolicyEditViewModel
{
    /// <summary>Gets or sets the original name when editing an existing profile.</summary>
    [StringLength(80)]
    public string? OriginalName { get; set; }

    /// <summary>Gets or sets the policy profile name.</summary>
    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the policy strictness.</summary>
    [Required]
    [RegularExpression("Lenient|Standard|Strict|SecurityCritical", ErrorMessage = "Choose a supported strictness value.")]
    public string Strictness { get; set; } = "Standard";

    /// <summary>Gets or sets the operator-facing policy description.</summary>
    [StringLength(400)]
    public string Description { get; set; } = string.Empty;
}
