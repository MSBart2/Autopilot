namespace Cyberpilot.Git;

/// <summary>
/// Creates or reuses the stable branch for an Cyberpilot issue run.
/// </summary>
public interface IBranchProvisioner
{
    /// <summary>
    /// Ensures a deterministic issue branch exists in the local repository.
    /// </summary>
    /// <param name="repository">The repository in owner/name form.</param>
    /// <param name="issueNumber">The GitHub issue number.</param>
    /// <param name="issueTitle">The issue title used to build the branch slug.</param>
    /// <param name="repoRoot">The repository root.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provisioned branch information.</returns>
    Task<CyberpilotBranchInfo> EnsureBranchAsync(string repository, int issueNumber, string issueTitle, string repoRoot, CancellationToken cancellationToken = default);
}
