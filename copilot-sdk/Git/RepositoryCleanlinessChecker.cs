using System.Diagnostics;

namespace Cyberpilot.Git;

/// <summary>
/// Checks whether a git repository has a clean working tree (no uncommitted changes).
/// </summary>
internal interface IRepositoryCleanlinessChecker
{
    /// <summary>
    /// Checks if the repository has a clean working tree.
    /// </summary>
    /// <param name="repoRoot">The repository root path.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A result indicating whether the repository is clean.</returns>
    Task<RepositoryCleanlinessResult> CheckAsync(string repoRoot, CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of a repository cleanliness check.
/// </summary>
/// <param name="IsClean">Whether the repository has no uncommitted changes.</param>
/// <param name="Error">Details about dirty files when IsClean is false.</param>
internal sealed record RepositoryCleanlinessResult(bool IsClean, string? Error)
{
    public static RepositoryCleanlinessResult Clean { get; } = new(true, null);

    public static RepositoryCleanlinessResult Dirty(string error)
    {
        return new RepositoryCleanlinessResult(false, error);
    }
}

/// <summary>
/// Checks repository cleanliness by running git status.
/// </summary>
internal sealed class GitRepositoryCleanlinessChecker : IRepositoryCleanlinessChecker
{
    public async Task<RepositoryCleanlinessResult> CheckAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunGitAsync(repoRoot, ["status", "--porcelain"], cancellationToken);
            if (result.ExitCode != 0)
            {
                return RepositoryCleanlinessResult.Dirty(
                    $"Failed to check repository status. git status exited with code {result.ExitCode}.");
            }

            var dirtyFiles = result.StandardOutput.Trim();
            if (!string.IsNullOrEmpty(dirtyFiles))
            {
                return RepositoryCleanlinessResult.Dirty(
                    $"Dirty files:\n{dirtyFiles}\n\nRun 'git status' to see details, or commit/stash changes before retrying.");
            }

            return RepositoryCleanlinessResult.Clean;
        }
        catch (Exception ex)
        {
            return RepositoryCleanlinessResult.Dirty(
                $"Exception checking repository cleanliness: {ex.Message}");
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");
        
        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, standardOutput, standardError);
    }
}
