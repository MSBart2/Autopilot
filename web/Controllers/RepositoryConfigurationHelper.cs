using Cyberpilot.GitHub;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.Extensions.Options;

namespace Cyberpilot.Web.Controllers;

/// <summary>
/// Helper for resolving repository configurations and paths.
/// </summary>
internal sealed class RepositoryConfigurationHelper
{
    private readonly CyberpilotWebOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger _logger;

    public RepositoryConfigurationHelper(CyberpilotWebOptions options, IWebHostEnvironment environment, ILogger logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Gets the list of configured repositories available for selection.
    /// </summary>
    public IReadOnlyList<ConfiguredRepositoryViewModel> GetConfiguredRepositoryChoices()
    {
        return _options.Repositories
            .Select(repository => TryBuildConfiguredRepository(repository, out var configured) ? configured : null)
            .Where(repository => repository is not null)
            .Select(repository => new ConfiguredRepositoryViewModel(
                string.IsNullOrWhiteSpace(repository!.Name) ? repository.Repository : repository.Name,
                repository.Repository,
                repository.RepoRoot))
            .DistinctBy(repository => repository.Repository, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Tries to get the default configured repository with a token.
    /// </summary>
    public bool TryGetDefaultConfiguredRepository(out RuntimeConfiguredRepository repository)
    {
        repository = default!;
        var configuredRepositories = _options.Repositories
            .Select(option => TryBuildConfiguredRepository(option, out var configured) ? configured : null)
            .Where(configured => configured is not null && !string.IsNullOrWhiteSpace(configured.Token))
            .Cast<RuntimeConfiguredRepository>()
            .ToArray();

        if (configuredRepositories.Length == 0)
        {
            return false;
        }

        repository = configuredRepositories.FirstOrDefault(configured =>
            configured.Repository.Equals(_options.Repository, StringComparison.OrdinalIgnoreCase))
            ?? configuredRepositories[0];
        return true;
    }

    /// <summary>
    /// Tries to get a configured repository by name.
    /// </summary>
    public bool TryGetConfiguredRepository(string repository, out RuntimeConfiguredRepository configuredRepository)
    {
        configuredRepository = default!;
        _logger.LogDebug("TryGetConfiguredRepository: Looking for {Repository} among {Count} configured repositories", repository, _options.Repositories.Count);
        foreach (var option in _options.Repositories)
        {
            if (TryBuildConfiguredRepository(option, out var configured)
                && configured.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(configured.Token))
            {
                _logger.LogDebug("TryGetConfiguredRepository: Found matching configured repository {ConfiguredRepo}", configured.Repository);
                configuredRepository = configured;
                return true;
            }
        }

        _logger.LogDebug("TryGetConfiguredRepository: No matching configured repository found for {Repository}", repository);
        return false;
    }

    /// <summary>
    /// Resolves the repository root path.
    /// </summary>
    public string ResolveRepoRoot(string? repoRoot)
    {
        var value = string.IsNullOrWhiteSpace(repoRoot) ? _options.RepoRoot : repoRoot;
        return Path.GetFullPath(value);
    }

    /// <summary>
    /// Resolves the agent prompt root path.
    /// </summary>
    public string ResolveAgentPromptRoot()
    {
        var value = string.IsNullOrWhiteSpace(_options.AgentPromptRoot)
            ? Path.Combine(_environment.ContentRootPath, "..")
            : _options.AgentPromptRoot;
        return Path.GetFullPath(value);
    }

    /// <summary>
    /// Resolves a GitHub token for a repository from configuration or environment.
    /// </summary>
    public string? ResolveRemoteToken(string repository)
    {
        _logger.LogDebug("ResolveRemoteToken: Checking for configured repository {Repository}", repository);
        if (TryGetConfiguredRepository(repository, out var configured) && !string.IsNullOrWhiteSpace(configured.Token))
        {
            _logger.LogInformation("ResolveRemoteToken: Found configured repository {Repository}", repository);
            return configured.Token;
        }

        _logger.LogDebug("ResolveRemoteToken: {Repository} not found in configured repositories, checking environment variables", repository);
        var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            _logger.LogInformation("ResolveRemoteToken: Found token in environment variables for {Repository}", repository);
            return envToken;
        }

        _logger.LogWarning("ResolveRemoteToken: No token found for {Repository} in config or environment", repository);
        return null;
    }

    private bool TryBuildConfiguredRepository(ConfiguredRepositoryOptions option, out RuntimeConfiguredRepository configuredRepository)
    {
        configuredRepository = default!;
        if (!GitHubRepositoryParser.TryNormalize(option.Repository, out var repository))
        {
            return false;
        }

        configuredRepository = new RuntimeConfiguredRepository(option.Name, repository, ResolveRepoRoot(option.RepoRoot), option.Token);
        return true;
    }
}

/// <summary>
/// Runtime representation of a configured repository with resolved paths.
/// </summary>
internal sealed record RuntimeConfiguredRepository(string Name, string Repository, string RepoRoot, string Token);
