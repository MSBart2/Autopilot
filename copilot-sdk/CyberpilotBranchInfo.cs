namespace Cyberpilot;

/// <summary>
/// Describes the stable branch selected for an Cyberpilot run.
/// </summary>
public sealed record CyberpilotBranchInfo(
    string BranchName,
    bool WasCreated,
    bool RemoteExists,
    string? WorktreePath);
