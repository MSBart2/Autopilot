using System.Diagnostics;
using System.Text;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Validates local git repository roots before SDK execution.
/// </summary>
public sealed class LocalRepositoryValidator : ILocalRepositoryValidator
{
    private readonly IGitCommandRunner gitCommandRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalRepositoryValidator"/> class.
    /// </summary>
    public LocalRepositoryValidator()
        : this(new GitCommandRunner())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalRepositoryValidator"/> class.
    /// </summary>
    /// <param name="gitCommandRunner">The git command runner.</param>
    public LocalRepositoryValidator(IGitCommandRunner gitCommandRunner)
    {
        this.gitCommandRunner = gitCommandRunner;
    }

    /// <inheritdoc />
    public async Task<string> PrepareAsync(string repoRoot, string repository, string? githubToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        var normalizedRepoRoot = Path.GetFullPath(repoRoot);
        if (!Directory.Exists(normalizedRepoRoot))
        {
            await CloneAsync(normalizedRepoRoot, repository, githubToken, cancellationToken);
        }

        return await ValidateAsync(normalizedRepoRoot, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> ValidateAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        var normalizedRepoRoot = Path.GetFullPath(repoRoot);
        if (!Directory.Exists(normalizedRepoRoot))
        {
            throw new DirectoryNotFoundException($"Configured repository root does not exist: {normalizedRepoRoot}");
        }

        EnsureWritable(normalizedRepoRoot);
        await RunGitAsync(normalizedRepoRoot, ["rev-parse", "--is-inside-work-tree"], null, cancellationToken, "Configured repository root is not a git work tree");
        return normalizedRepoRoot;
    }

    private async Task CloneAsync(string repoRoot, string repository, string? githubToken, CancellationToken cancellationToken)
    {
        if (File.Exists(repoRoot))
        {
            throw new IOException($"Configured repository root points to a file: {repoRoot}");
        }

        var parentDirectory = Path.GetDirectoryName(repoRoot);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException($"Configured repository root does not have a parent directory: {repoRoot}");
        }

        Directory.CreateDirectory(parentDirectory);
        EnsureWritable(parentDirectory);
        var cloneUrl = $"https://github.com/{repository}.git";
        await RunGitAsync(
            parentDirectory,
            ["clone", cloneUrl, repoRoot],
            githubToken,
            cancellationToken,
            $"Failed to clone {repository} into {repoRoot}");
    }

    private static void EnsureWritable(string repoRoot)
    {
        var probePath = Path.Combine(repoRoot, $".cyberpilot-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probePath, string.Empty);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException($"Configured repository root is not writable: {repoRoot}", ex);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private async Task RunGitAsync(string repoRoot, IReadOnlyList<string> args, string? githubToken, CancellationToken cancellationToken, string failureMessage)
    {
        var result = await gitCommandRunner.RunAsync(repoRoot, args, githubToken, cancellationToken);
        if (result.ExitCode != 0)
        {
            var error = Sanitize(result.StandardError, githubToken);
            throw new InvalidOperationException($"{failureMessage} ({repoRoot}). git {string.Join(' ', args)} exited with code {result.ExitCode}: {error}");
        }
    }

    private static string Sanitize(string value, string? githubToken)
        => string.IsNullOrWhiteSpace(githubToken) ? value : value.Replace(githubToken, "[redacted]", StringComparison.Ordinal);
}

/// <summary>
/// Runs git commands for local repository preparation.
/// </summary>
public interface IGitCommandRunner
{
    /// <summary>
    /// Runs a git command.
    /// </summary>
    /// <param name="workingDirectory">The working directory for the git process.</param>
    /// <param name="args">The git arguments.</param>
    /// <param name="githubToken">The GitHub token for authenticated GitHub operations, when available.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The git command result.</returns>
    Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, string? githubToken = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of a git command.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">The standard output text.</param>
/// <param name="StandardError">The standard error text.</param>
public sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Process-backed git command runner.
/// </summary>
public sealed class GitCommandRunner : IGitCommandRunner
{
    /// <inheritdoc />
    public async Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, string? githubToken = null, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (!string.IsNullOrWhiteSpace(githubToken))
        {
            var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{githubToken}"));
            startInfo.Environment["GIT_CONFIG_COUNT"] = "1";
            startInfo.Environment["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader";
            startInfo.Environment["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {basicToken}";
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new GitCommandResult(process.ExitCode, await outputTask, await errorTask);
    }
}