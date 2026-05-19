namespace Cyberpilot.Pipeline;

/// <summary>
/// Configures runtime behavior that affects prompt guidance and diagnostic artifact capture.
/// </summary>
/// <param name="CommandStyle">The shell command style agents should prefer.</param>
/// <param name="CaptureToolOutputArtifacts">Whether shaped tool output should be persisted as diagnostic artifacts.</param>
/// <param name="SystemMessageMode">Controls whether harness law is injected as an SDK system message (append or replace) instead of repeating it in every user prompt.</param>
public sealed record CyberpilotRuntimePreferences(
    CommandStylePreference CommandStyle = CommandStylePreference.Auto,
    bool CaptureToolOutputArtifacts = false,
    HarnessSystemMessageMode SystemMessageMode = HarnessSystemMessageMode.None)
{
    /// <summary>Gets default runtime preferences.</summary>
    public static CyberpilotRuntimePreferences Default { get; } = new();
}

/// <summary>
/// Controls how the Cyberpilot harness law is delivered to the SDK session.
/// </summary>
public enum HarnessSystemMessageMode
{
    /// <summary>Harness law is included inline in every user prompt (default).</summary>
    None,

    /// <summary>Harness law is injected as an appended SDK system message, preserving built-in Copilot guidance.</summary>
    Append,

    /// <summary>Harness law is injected as a replacement SDK system message, removing built-in Copilot guidance.</summary>
    Replace,
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
