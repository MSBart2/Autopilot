using System.Text.Json;
using Cyberpilot.GitHub;
using Cyberpilot.Options;

namespace Cyberpilot;

internal sealed class SdkConfiguration
{
    private readonly IReadOnlyList<SdkRepositoryConnection> repositories;

    private SdkConfiguration(string? defaultRepository, IReadOnlyList<SdkRepositoryConnection> repositories)
    {
        DefaultRepository = defaultRepository;
        this.repositories = repositories;
    }

    public string? DefaultRepository { get; }

    public static SdkConfiguration Load(string? configPath, string repoRoot)
    {
        var sources = DiscoverConfigFiles(configPath, repoRoot);
        string? defaultRepository = null;
        var repositories = new Dictionary<string, SdkRepositoryConnection>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            LoadJsonFile(source, ref defaultRepository, repositories);
        }

        LoadEnvironment(ref defaultRepository, repositories);
        return new SdkConfiguration(defaultRepository, repositories.Values.ToArray());
    }

    public CyberpilotOptions ApplyTo(CyberpilotOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Repository))
        {
            var tokenRepository = FindRepository(options.Repository);
            return tokenRepository is null || string.IsNullOrWhiteSpace(tokenRepository.RepoRoot)
                ? options
                : options with { RepoRoot = Path.GetFullPath(tokenRepository.RepoRoot) };
        }

        var configuredRepository = FindRepository(DefaultRepository) ?? repositories.FirstOrDefault();
        if (configuredRepository is null || string.IsNullOrWhiteSpace(configuredRepository.Repository))
        {
            return options;
        }

        return string.IsNullOrWhiteSpace(configuredRepository.RepoRoot)
            ? options with { Repository = configuredRepository.Repository }
            : options with { Repository = configuredRepository.Repository, RepoRoot = Path.GetFullPath(configuredRepository.RepoRoot) };
    }

    public string? GetToken(string? repository)
    {
        if (!GitHubRepositoryParser.TryNormalize(repository, out var normalizedRepository))
        {
            return null;
        }

        return repositories.FirstOrDefault(item => item.Repository.Equals(normalizedRepository, StringComparison.OrdinalIgnoreCase))?.Token;
    }

    private SdkRepositoryConnection? FindRepository(string? repository)
    {
        return GitHubRepositoryParser.TryNormalize(repository, out var normalizedRepository)
            ? repositories.FirstOrDefault(item => item.Repository.Equals(normalizedRepository, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private static IReadOnlyList<string> DiscoverConfigFiles(string? configPath, string repoRoot)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var fullPath = Path.GetFullPath(configPath);
            return File.Exists(fullPath) ? [fullPath] : [];
        }

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Development.json"),
            Path.Combine(repoRoot, "web", "appsettings.json"),
            Path.Combine(repoRoot, "web", "appsettings.Development.json"),
        };

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToArray();
    }

    private static void LoadJsonFile(string path, ref string? defaultRepository, Dictionary<string, SdkRepositoryConnection> repositories)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("Cyberpilot", out var cyberpilot))
        {
            return;
        }

        if (cyberpilot.TryGetProperty("Repository", out var repositoryElement)
            && GitHubRepositoryParser.TryNormalize(repositoryElement.GetString(), out var repository))
        {
            defaultRepository = repository;
        }

        if (!cyberpilot.TryGetProperty("Repositories", out var repositoriesElement)
            || repositoriesElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in repositoriesElement.EnumerateArray())
        {
            AddRepository(
                item.GetPropertyOrDefault("Name"),
                item.GetPropertyOrDefault("Repository"),
                item.GetPropertyOrDefault("RepoRoot"),
                item.GetPropertyOrDefault("Token"),
                repositories);
        }
    }

    private static void LoadEnvironment(ref string? defaultRepository, Dictionary<string, SdkRepositoryConnection> repositories)
    {
        if (GitHubRepositoryParser.TryNormalize(Environment.GetEnvironmentVariable("Cyberpilot__Repository"), out var repository))
        {
            defaultRepository = repository;
        }

        for (var index = 0; index < 50; index++)
        {
            var prefix = $"Cyberpilot__Repositories__{index}__";
            AddRepository(
                Environment.GetEnvironmentVariable(prefix + "Name"),
                Environment.GetEnvironmentVariable(prefix + "Repository"),
                Environment.GetEnvironmentVariable(prefix + "RepoRoot"),
                Environment.GetEnvironmentVariable(prefix + "Token"),
                repositories);
        }
    }

    private static void AddRepository(string? name, string? repositoryInput, string? repoRoot, string? token, Dictionary<string, SdkRepositoryConnection> repositories)
    {
        if (!GitHubRepositoryParser.TryNormalize(repositoryInput, out var repository))
        {
            return;
        }

        if (repositories.TryGetValue(repository, out var existing))
        {
            repositories[repository] = existing with
            {
                Name = string.IsNullOrWhiteSpace(name) ? existing.Name : name,
                RepoRoot = string.IsNullOrWhiteSpace(repoRoot) ? existing.RepoRoot : repoRoot,
                Token = string.IsNullOrWhiteSpace(token) ? existing.Token : token,
            };
            return;
        }

        repositories[repository] = new SdkRepositoryConnection(name ?? string.Empty, repository, repoRoot ?? string.Empty, token ?? string.Empty);
    }
}

internal sealed record SdkRepositoryConnection(string Name, string Repository, string RepoRoot, string Token);

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
    }
}