namespace Cyberpilot.Pipeline;

/// <summary>
/// Configures runtime behavior that affects prompt guidance and diagnostic artifact capture.
/// </summary>
/// <param name="CommandStyle">The shell command style agents should prefer.</param>
/// <param name="CaptureToolOutputArtifacts">Whether shaped tool output should be persisted as diagnostic artifacts.</param>
/// <param name="UseHarnessSystemMessage">When true, injects harness law (output rules, command guidance, JSON safety) as an appended SDK system message instead of repeating it in every user prompt.</param>
public sealed record CyberpilotRuntimePreferences(
    CommandStylePreference CommandStyle = CommandStylePreference.Auto,
    bool CaptureToolOutputArtifacts = false,
    bool UseHarnessSystemMessage = false)
{
    /// <summary>Gets default runtime preferences.</summary>
    public static CyberpilotRuntimePreferences Default { get; } = new();
}

/// <summary>
/// Indicates which shell command style Cyberpilot should ask agents to prefer.
/// </summary>
public enum CommandStylePreference
{
    /// <summary>Prefer command syntax for the current host operating system.</summary>
    Auto,

    /// <summary>Prefer PowerShell-native Windows command syntax.</summary>
    Windows,

    /// <summary>Prefer POSIX/Linux shell command syntax.</summary>
    Linux,
}
