using System.Text.Json;
using Cyberpilot.GitHub;
using Cyberpilot.Options;
using Cyberpilot.Pipeline;

namespace Cyberpilot;

internal sealed class SdkConfiguration
{
    private readonly IReadOnlyList<SdkRepositoryConnection> repositories;

    private SdkConfiguration(string? defaultRepository, IReadOnlyList<SdkRepositoryConnection> repositories, CyberpilotRuntimePreferences? runtimePreferences,
        string? agentPromptRoot, string? databaseConnectionString, bool? approveAll, int? benchmarkIssue, string? benchmarkRepository)
    {
        DefaultRepository = defaultRepository;
        this.repositories = repositories;
        RuntimePreferences = runtimePreferences ?? CyberpilotRuntimePreferences.Default;
        AgentPromptRoot = agentPromptRoot;
        DatabaseConnectionString = databaseConnectionString;
        ApproveAll = approveAll;
        BenchmarkIssue = benchmarkIssue;
        BenchmarkRepository = benchmarkRepository;
    }

    public string? DefaultRepository { get; }

    public CyberpilotRuntimePreferences RuntimePreferences { get; }

    public string? AgentPromptRoot { get; }

    public string? DatabaseConnectionString { get; }

    public bool? ApproveAll { get; }

    public int? BenchmarkIssue { get; }

    public string? BenchmarkRepository { get; }

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
        CyberpilotRuntimePreferences? runtimePreferences = null;
        string? agentPromptRoot = null;
        string? databaseConnectionString = null;
        bool? approveAll = null;
        int? benchmarkIssue = null;
        string? benchmarkRepository = null;
        foreach (var source in sources)
        {
            LoadRuntimePreferences(source, ref runtimePreferences);
            LoadGlobalDefaults(source, ref agentPromptRoot, ref databaseConnectionString, ref approveAll, ref benchmarkIssue, ref benchmarkRepository);
        }

        LoadRuntimePreferencesFromEnvironment(ref runtimePreferences);
        return new SdkConfiguration(defaultRepository, repositories.Values.ToArray(), runtimePreferences, agentPromptRoot, databaseConnectionString, approveAll, benchmarkIssue, benchmarkRepository);
    }

    public CyberpilotOptions ApplyTo(CyberpilotOptions options)
    {
        options = ApplyGlobalDefaults(options);

        if (!string.IsNullOrWhiteSpace(options.Repository))
        {
            var tokenRepository = FindRepository(options.Repository);
            return tokenRepository is null || string.IsNullOrWhiteSpace(tokenRepository.RepoRoot)
                ? options with { RuntimePreferences = MergeRuntimePreferences(options.RuntimePreferences) }
                : options with { RepoRoot = Path.GetFullPath(tokenRepository.RepoRoot), RuntimePreferences = MergeRuntimePreferences(options.RuntimePreferences) };
        }

        var configuredRepository = FindRepository(DefaultRepository) ?? repositories.FirstOrDefault();
        if (configuredRepository is null || string.IsNullOrWhiteSpace(configuredRepository.Repository))
        {
            return options with { RuntimePreferences = MergeRuntimePreferences(options.RuntimePreferences) };
        }

        return string.IsNullOrWhiteSpace(configuredRepository.RepoRoot)
            ? options with { Repository = configuredRepository.Repository, RuntimePreferences = MergeRuntimePreferences(options.RuntimePreferences) }
            : options with { Repository = configuredRepository.Repository, RepoRoot = Path.GetFullPath(configuredRepository.RepoRoot), RuntimePreferences = MergeRuntimePreferences(options.RuntimePreferences) };
    }

    private CyberpilotOptions ApplyGlobalDefaults(CyberpilotOptions options)
    {
        if (AgentPromptRoot is not null && options.AgentPromptRoot is null)
        {
            options = options with { AgentPromptRoot = Path.GetFullPath(AgentPromptRoot) };
        }

        if (DatabaseConnectionString is not null && options.DatabaseConnectionString is null)
        {
            options = options with { DatabaseConnectionString = DatabaseConnectionString };
        }

        if (ApproveAll == true && !options.ApproveAll)
        {
            options = options with { ApproveAll = true };
        }

        if (BenchmarkIssue is > 0 && options.IssueNumber <= 0)
        {
            options = options with { IssueNumber = BenchmarkIssue.Value };
        }

        if (BenchmarkRepository is not null && string.IsNullOrWhiteSpace(options.Repository))
        {
            options = options with { Repository = BenchmarkRepository };
        }

        return options;
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

    private CyberpilotRuntimePreferences MergeRuntimePreferences(CyberpilotRuntimePreferences? optionPreferences)
    {
        if (optionPreferences is null)
        {
            return RuntimePreferences;
        }

        return optionPreferences with
        {
            CommandStyle = optionPreferences.CommandStyle == CommandStylePreference.Auto
                ? RuntimePreferences.CommandStyle
                : optionPreferences.CommandStyle,
            CaptureToolOutputArtifacts = optionPreferences.CaptureToolOutputArtifacts || RuntimePreferences.CaptureToolOutputArtifacts,
            SystemMessageMode = optionPreferences.SystemMessageModeConfigured
                ? optionPreferences.SystemMessageMode
                : RuntimePreferences.SystemMessageMode,
            SystemMessageModeConfigured = optionPreferences.SystemMessageModeConfigured,
            SystemMessageProfile = optionPreferences.SystemMessageProfileConfigured
                ? optionPreferences.SystemMessageProfile
                : RuntimePreferences.SystemMessageProfile,
            SystemMessageProfileConfigured = optionPreferences.SystemMessageProfileConfigured,
            StageSystemMessages = optionPreferences.SystemMessageModeConfigured || optionPreferences.SystemMessageProfileConfigured
                ? null
                : RuntimePreferences.StageSystemMessages,
        };
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

    private static void LoadRuntimePreferences(string path, ref CyberpilotRuntimePreferences? runtimePreferences)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("Cyberpilot", out var cyberpilot))
        {
            return;
        }

        var current = runtimePreferences ?? CyberpilotRuntimePreferences.Default;
        if (cyberpilot.TryGetProperty("CommandStyle", out var commandStyle)
            && TryParseCommandStyle(commandStyle.GetString(), out var parsedStyle))
        {
            current = current with { CommandStyle = parsedStyle };
        }

        if (cyberpilot.TryGetProperty("CaptureToolOutputArtifacts", out var captureToolOutput)
            && captureToolOutput.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            current = current with { CaptureToolOutputArtifacts = captureToolOutput.GetBoolean() };
        }

        if (cyberpilot.TryGetProperty("SystemMessageMode", out var systemMessageModeEl)
            && TryParseSystemMessageMode(systemMessageModeEl.GetString(), out var parsedMode))
        {
            current = current with { SystemMessageMode = parsedMode };
        }
        else if (cyberpilot.TryGetProperty("UseHarnessSystemMessage", out var useHarnessSystemMessage)
            && useHarnessSystemMessage.ValueKind is JsonValueKind.True or JsonValueKind.False
            && useHarnessSystemMessage.GetBoolean())
        {
            current = current with { SystemMessageMode = HarnessSystemMessageMode.Append };
        }

        if (cyberpilot.TryGetProperty("SystemMessageProfile", out var systemMessageProfileEl)
            && TryParseSystemMessageProfile(systemMessageProfileEl.GetString(), out var parsedProfile))
        {
            current = current with { SystemMessageProfile = parsedProfile };
        }

        if (cyberpilot.TryGetProperty("StageSystemMessages", out var stageSystemMessagesEl)
            && stageSystemMessagesEl.ValueKind == JsonValueKind.Object)
        {
            current = current with { StageSystemMessages = ParseStageSystemMessages(stageSystemMessagesEl) };
        }

        runtimePreferences = current;
    }

    private static void LoadGlobalDefaults(string path, ref string? agentPromptRoot, ref string? databaseConnectionString, ref bool? approveAll, ref int? benchmarkIssue, ref string? benchmarkRepository)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("Cyberpilot", out var cyberpilot))
        {
            return;
        }

        if (cyberpilot.TryGetProperty("AgentPromptRoot", out var agentPromptRootEl))
        {
            var value = agentPromptRootEl.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                agentPromptRoot = value;
            }
        }

        if (cyberpilot.TryGetProperty("Database", out var databaseEl))
        {
            var value = databaseEl.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                databaseConnectionString = value;
            }
        }

        if (cyberpilot.TryGetProperty("ApproveAll", out var approveAllEl)
            && approveAllEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            approveAll = approveAllEl.GetBoolean();
        }

        if (cyberpilot.TryGetProperty("BenchmarkIssue", out var benchmarkIssueEl)
            && benchmarkIssueEl.TryGetInt32(out var benchmarkIssueValue)
            && benchmarkIssueValue > 0)
        {
            benchmarkIssue = benchmarkIssueValue;
        }

        if (cyberpilot.TryGetProperty("BenchmarkRepository", out var benchmarkRepositoryEl)
            && GitHubRepositoryParser.TryNormalize(benchmarkRepositoryEl.GetString(), out var normalizedBenchmarkRepository))
        {
            benchmarkRepository = normalizedBenchmarkRepository;
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

    private static void LoadRuntimePreferencesFromEnvironment(ref CyberpilotRuntimePreferences? runtimePreferences)
    {
        var current = runtimePreferences ?? CyberpilotRuntimePreferences.Default;
        if (TryParseCommandStyle(Environment.GetEnvironmentVariable("Cyberpilot__CommandStyle"), out var commandStyle))
        {
            current = current with { CommandStyle = commandStyle };
        }

        if (bool.TryParse(Environment.GetEnvironmentVariable("Cyberpilot__CaptureToolOutputArtifacts"), out var captureToolOutputArtifacts))
        {
            current = current with { CaptureToolOutputArtifacts = captureToolOutputArtifacts };
        }

        if (TryParseSystemMessageMode(Environment.GetEnvironmentVariable("Cyberpilot__SystemMessageMode"), out var systemMessageMode))
        {
            current = current with { SystemMessageMode = systemMessageMode };
        }
        else if (bool.TryParse(Environment.GetEnvironmentVariable("Cyberpilot__UseHarnessSystemMessage"), out var useHarnessSystemMessage) && useHarnessSystemMessage)
        {
            current = current with { SystemMessageMode = HarnessSystemMessageMode.Append };
        }

        if (TryParseSystemMessageProfile(Environment.GetEnvironmentVariable("Cyberpilot__SystemMessageProfile"), out var systemMessageProfile))
        {
            current = current with { SystemMessageProfile = systemMessageProfile };
        }

        runtimePreferences = current;
    }

    private static bool TryParseCommandStyle(string? value, out CommandStylePreference commandStyle)
    {
        commandStyle = CommandStylePreference.Auto;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        commandStyle = value.Trim().ToLowerInvariant() switch
        {
            "auto" => CommandStylePreference.Auto,
            "windows" or "powershell" or "pwsh" => CommandStylePreference.Windows,
            "linux" or "posix" or "bash" or "shell" => CommandStylePreference.Linux,
            _ => CommandStylePreference.Auto,
        };

        return value.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)
            || commandStyle != CommandStylePreference.Auto;
    }

    private static bool TryParseSystemMessageMode(string? value, out HarnessSystemMessageMode mode)
    {
        mode = HarnessSystemMessageMode.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        mode = value.Trim().ToLowerInvariant() switch
        {
            "none" => HarnessSystemMessageMode.None,
            "append" => HarnessSystemMessageMode.Append,
            "replace" => HarnessSystemMessageMode.Replace,
            _ => HarnessSystemMessageMode.None,
        };

        return !value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
            ? mode != HarnessSystemMessageMode.None
            : true;
    }

    private static IReadOnlyDictionary<string, HarnessStageSystemMessage> ParseStageSystemMessages(JsonElement element)
    {
        var values = new Dictionary<string, HarnessStageSystemMessage>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in element.EnumerateObject())
        {
            if (stage.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var mode = HarnessSystemMessageMode.None;
            if (stage.Value.TryGetProperty("Mode", out var modeEl))
            {
                TryParseSystemMessageMode(modeEl.GetString(), out mode);
            }

            var profile = HarnessSystemMessageProfile.Full;
            if (stage.Value.TryGetProperty("Profile", out var profileEl))
            {
                TryParseSystemMessageProfile(profileEl.GetString(), out profile);
            }

            values[stage.Name] = new HarnessStageSystemMessage(mode, profile);
        }

        return values;
    }

    private static bool TryParseSystemMessageProfile(string? value, out HarnessSystemMessageProfile profile)
    {
        profile = HarnessSystemMessageProfile.Full;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        profile = value.Trim().ToLowerInvariant() switch
        {
            "full" => HarnessSystemMessageProfile.Full,
            "lean" => HarnessSystemMessageProfile.Lean,
            _ => HarnessSystemMessageProfile.Full,
        };

        return value.Trim().Equals("full", StringComparison.OrdinalIgnoreCase)
            || profile != HarnessSystemMessageProfile.Full;
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
