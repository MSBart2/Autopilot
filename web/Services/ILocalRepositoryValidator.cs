namespace Cyberpilot.Web.Services;

/// <summary>
/// Prepares and validates local git repository roots used by web-triggered SDK runs.
/// </summary>
public interface ILocalRepositoryValidator
{
    /// <summary>
    /// Ensures a repository root exists locally, cloning it when needed, and returns its full path.
    /// </summary>
    /// <param name="repoRoot">The configured local repository root.</param>
    /// <param name="repository">The GitHub repository in owner/name format.</param>
    /// <param name="githubToken">The GitHub token used for private repository cloning, when available.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized repository root path.</returns>
    Task<string> PrepareAsync(string repoRoot, string repository, string? githubToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a local repository root and returns its full path.
    /// </summary>
    /// <param name="repoRoot">The configured local repository root.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized repository root path.</returns>
    Task<string> ValidateAsync(string repoRoot, CancellationToken cancellationToken = default);
}