namespace Cyberpilot.Pipeline;

/// <summary>
/// Configures runtime behavior that affects prompt guidance and diagnostic artifact capture.
/// </summary>
/// <param name="CommandStyle">The shell command style agents should prefer.</param>
/// <param name="CaptureToolOutputArtifacts">Whether detailed redacted tool output should be persisted as diagnostic artifacts.</param>
/// <param name="SystemMessageMode">Controls whether harness law is injected as an SDK system message (append or replace) instead of repeating it in every user prompt.</param>
/// <param name="SystemMessageProfile">Controls how verbose the SDK system message harness guidance should be.</param>
/// <param name="StageSystemMessages">Per-stage system message defaults used when the CLI does not explicitly set system-message options.</param>
/// <param name="SystemMessageModeConfigured">Whether the system-message mode was explicitly configured by the current invocation.</param>
/// <param name="SystemMessageProfileConfigured">Whether the system-message profile was explicitly configured by the current invocation.</param>
public sealed record CyberpilotRuntimePreferences(
    CommandStylePreference CommandStyle = CommandStylePreference.Auto,
    bool CaptureToolOutputArtifacts = false,
    HarnessSystemMessageMode SystemMessageMode = HarnessSystemMessageMode.None,
    HarnessSystemMessageProfile SystemMessageProfile = HarnessSystemMessageProfile.Full,
    IReadOnlyDictionary<string, HarnessStageSystemMessage>? StageSystemMessages = null,
    bool SystemMessageModeConfigured = false,
    bool SystemMessageProfileConfigured = false)
{
    /// <summary>Gets default runtime preferences.</summary>
    public static CyberpilotRuntimePreferences Default { get; } = new();

    /// <summary>Gets the effective system-message settings for a stage.</summary>
    public HarnessStageSystemMessage GetSystemMessageForStage(string stageName)
    {
        if (!SystemMessageModeConfigured
            && !SystemMessageProfileConfigured
            && StageSystemMessages is not null
            && StageSystemMessages.TryGetValue(stageName, out var stageOverride))
        {
            return stageOverride;
        }

        return new HarnessStageSystemMessage(SystemMessageMode, SystemMessageProfile);
    }
}

/// <summary>
/// Configures the system-message mode and profile for a specific stage.
/// </summary>
/// <param name="Mode">How harness law should be delivered for the stage.</param>
/// <param name="Profile">How verbose the system-message guidance should be.</param>
public sealed record HarnessStageSystemMessage(
    HarnessSystemMessageMode Mode = HarnessSystemMessageMode.None,
    HarnessSystemMessageProfile Profile = HarnessSystemMessageProfile.Full);

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
/// Controls the amount of harness guidance injected through the SDK system message.
/// </summary>
public enum HarnessSystemMessageProfile
{
    /// <summary>Use the complete harness law currently embedded in full user prompts.</summary>
    Full,

    /// <summary>Use a compact harness law focused on stage execution and final JSON validity.</summary>
    Lean,
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
