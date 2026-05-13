using Microsoft.Extensions.Caching.Memory;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Stores short-lived repository credentials for web-triggered runs.
/// </summary>
/// <param name="Id">The opaque connection identifier.</param>
/// <param name="Repository">The repository in owner/name form.</param>
/// <param name="RepoRoot">The local git repository root for SDK execution.</param>
/// <param name="Token">The GitHub token.</param>
public sealed record RepositoryConnection(string Id, string Repository, string RepoRoot, string Token);

/// <summary>
/// Stores repository credentials outside the pipeline database.
/// </summary>
public interface IRepositoryConnectionStore
{
    /// <summary>
    /// Saves a repository token and returns an opaque identifier for subsequent form posts.
    /// </summary>
    /// <param name="repository">The repository in owner/name form.</param>
    /// <param name="token">The GitHub token.</param>
    /// <returns>The connection identifier.</returns>
    string Save(string repository, string repoRoot, string token);

    /// <summary>
    /// Gets a repository connection by identifier.
    /// </summary>
    /// <param name="id">The connection identifier.</param>
    /// <returns>The connection when present.</returns>
    RepositoryConnection? Get(string? id);
}

/// <summary>
/// In-memory repository token store with short-lived entries.
/// </summary>
public sealed class RepositoryConnectionStore(IMemoryCache cache) : IRepositoryConnectionStore
{
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public string Save(string repository, string repoRoot, string token)
    {
        var id = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(id), new RepositoryConnection(id, repository, repoRoot, token), ConnectionLifetime);
        return id;
    }

    /// <inheritdoc />
    public RepositoryConnection? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return cache.TryGetValue(CacheKey(id), out RepositoryConnection? connection) ? connection : null;
    }

    private static string CacheKey(string id) => $"github-repository-connection:{id}";
}