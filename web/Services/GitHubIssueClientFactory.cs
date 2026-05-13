using Cyberpilot.GitHub;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Creates GitHub issue clients for a repository and token supplied at runtime.
/// </summary>
public interface IGitHubIssueClientFactory
{
    /// <summary>
    /// Creates an issue client for the specified repository.
    /// </summary>
    /// <param name="repository">The repository in owner/name form.</param>
    /// <param name="token">The GitHub token used for API calls.</param>
    /// <returns>A configured GitHub issue client.</returns>
    IGitHubIssueClient Create(string repository, string token);
}

/// <summary>
/// Default GitHub API issue client factory.
/// </summary>
public sealed class GitHubIssueClientFactory(IHttpClientFactory httpClientFactory) : IGitHubIssueClientFactory
{
    /// <inheritdoc />
    public IGitHubIssueClient Create(string repository, string token)
    {
        return new GitHubApiIssueClient(httpClientFactory.CreateClient("GitHubApi"), repository, token);
    }
}