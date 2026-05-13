namespace Cyberpilot.Web.Services;

/// <summary>
/// Configures the web-hosted Cyberpilot dashboard and runner.
/// </summary>
public sealed class CyberpilotWebOptions
{
    /// <summary>Gets or sets the configured repository in owner/name form.</summary>
    public string Repository { get; set; } = "rbmathis/Cyberpilot";

    /// <summary>Gets or sets named repositories that can be loaded from the issue launcher.</summary>
    public List<ConfiguredRepositoryOptions> Repositories { get; set; } = [];

    /// <summary>Gets or sets the repository root used by SDK runs.</summary>
    public string RepoRoot { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>Gets or sets the repository root that contains the Cyberpilot agent prompt files.</summary>
    public string AgentPromptRoot { get; set; } = string.Empty;

    /// <summary>Gets or sets whether web-triggered runs approve all Copilot tool permission requests.</summary>
    public bool ApproveAll { get; set; }

    /// <summary>Gets or sets whether web-triggered runs create missing SDK labels before running.</summary>
    public bool EnsureLabels { get; set; } = true;
}

/// <summary>
/// Configures one repository/token pair for the web issue launcher.
/// </summary>
public sealed class ConfiguredRepositoryOptions
{
    /// <summary>Gets or sets the display name shown in the launcher.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the repository URL or owner/name value.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Gets or sets the local git repository root used for SDK execution.</summary>
    public string RepoRoot { get; set; } = string.Empty;

    /// <summary>Gets or sets the GitHub token used for this repository.</summary>
    public string Token { get; set; } = string.Empty;
}
