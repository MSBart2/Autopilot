using System.Text.Json;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Describes detected target repository conventions for a Cyberpilot run.
/// </summary>
/// <param name="Languages">Detected ecosystem or language families.</param>
/// <param name="BuildCommands">Likely build commands for the repository.</param>
/// <param name="TestCommands">Likely test commands for the repository.</param>
/// <param name="DocumentationPaths">Likely documentation entry points.</param>
public sealed record RepositoryProfile(
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> BuildCommands,
    IReadOnlyList<string> TestCommands,
    IReadOnlyList<string> DocumentationPaths)
{
    /// <summary>Gets an empty repository profile.</summary>
    public static RepositoryProfile Empty { get; } = new([], [], [], []);

    /// <summary>Gets whether any repository convention was detected.</summary>
    public bool HasSignals => Languages.Count > 0 || BuildCommands.Count > 0 || TestCommands.Count > 0 || DocumentationPaths.Count > 0;

    /// <summary>Formats the profile as a compact operator-facing summary.</summary>
    /// <returns>A compact summary of the detected repository profile.</returns>
    public string ToSummary()
    {
        if (!HasSignals)
        {
            return "Repository profile detected: no build, test, or documentation conventions found.";
        }

        var parts = new List<string>();
        if (Languages.Count > 0) parts.Add($"languages: {string.Join(", ", Languages)}");
        if (BuildCommands.Count > 0) parts.Add($"build: {string.Join("; ", BuildCommands)}");
        if (TestCommands.Count > 0) parts.Add($"test: {string.Join("; ", TestCommands)}");
        if (DocumentationPaths.Count > 0) parts.Add($"docs: {string.Join(", ", DocumentationPaths)}");
        return $"Repository profile detected: {string.Join(" | ", parts)}.";
    }
}

/// <summary>
/// Detects target repository build, test, and documentation conventions before SDK execution.
/// </summary>
public interface IRepositoryProfileDetector
{
    /// <summary>Detects repository conventions from a local repository root.</summary>
    /// <param name="repoRoot">The local repository root.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The detected repository profile.</returns>
    Task<RepositoryProfile> DetectAsync(string repoRoot, CancellationToken cancellationToken = default);
}

/// <summary>
/// File-system backed detector for common repository conventions.
/// </summary>
public sealed class RepositoryProfileDetector : IRepositoryProfileDetector
{
    /// <inheritdoc />
    public Task<RepositoryProfile> DetectAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedRoot = Path.GetFullPath(repoRoot);
        var languages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var buildCommands = new List<string>();
        var testCommands = new List<string>();
        var documentationPaths = new List<string>();

        DetectDotNet(normalizedRoot, languages, buildCommands, testCommands);
        DetectNode(normalizedRoot, languages, buildCommands, testCommands);
        DetectDocumentation(normalizedRoot, documentationPaths);

        return Task.FromResult(new RepositoryProfile(
            languages.ToArray(),
            buildCommands.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            testCommands.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            documentationPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    private static void DetectDotNet(string repoRoot, SortedSet<string> languages, List<string> buildCommands, List<string> testCommands)
    {
        var solution = Directory.EnumerateFiles(repoRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (solution is not null)
        {
            languages.Add(".NET");
            buildCommands.Add($"dotnet build ./{solution}");
            testCommands.Add($"dotnet test ./{solution}");
            return;
        }

        var project = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (project is not null)
        {
            languages.Add(".NET");
            buildCommands.Add($"dotnet build ./{project}");
            testCommands.Add($"dotnet test ./{project}");
        }
    }

    private static void DetectNode(string repoRoot, SortedSet<string> languages, List<string> buildCommands, List<string> testCommands)
    {
        var packageJsonPath = Path.Combine(repoRoot, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return;
        }

        languages.Add("Node.js");
        using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        if (!document.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (scripts.TryGetProperty("build", out _))
        {
            buildCommands.Add("npm run build");
        }

        if (scripts.TryGetProperty("test", out _))
        {
            testCommands.Add("npm test");
        }
    }

    private static void DetectDocumentation(string repoRoot, List<string> documentationPaths)
    {
        if (File.Exists(Path.Combine(repoRoot, "README.md")))
        {
            documentationPaths.Add("README.md");
        }

        if (Directory.Exists(Path.Combine(repoRoot, "docs")))
        {
            documentationPaths.Add("docs/");
        }
    }
}