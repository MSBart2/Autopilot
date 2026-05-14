using System.Diagnostics;

namespace Cyberpilot.Web.Controllers;

/// <summary>
/// Git command helpers for branch management and cleanup operations.
/// </summary>
internal static class GitHelper
{
    /// <summary>
    /// Deletes an issue branch from both remote and local repositories.
    /// </summary>
    /// <param name="repoRoot">Repository root directory.</param>
    /// <param name="branchName">Branch name to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if either remote or local branch was deleted successfully.</returns>
    public static async Task<bool> DeleteIssueBranchAsync(string repoRoot, string branchName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchName) || !Directory.Exists(repoRoot))
        {
            return false;
        }

        var deletedRemote = await GitSucceedsAsync(repoRoot, ["push", "origin", "--delete", branchName], cancellationToken);
        var currentBranch = (await RunGitAsync(repoRoot, ["branch", "--show-current"], true, cancellationToken)).Trim();
        if (currentBranch.Equals(branchName, StringComparison.OrdinalIgnoreCase))
        {
            var defaultBranch = await ResolveDefaultBranchAsync(repoRoot, cancellationToken);
            await GitSucceedsAsync(repoRoot, ["switch", defaultBranch], cancellationToken);
        }

        var deletedLocal = await GitSucceedsAsync(repoRoot, ["branch", "-D", branchName], cancellationToken);
        return deletedRemote || deletedLocal;
    }

    /// <summary>
    /// Resolves the default branch name for a repository.
    /// </summary>
    /// <param name="repoRoot">Repository root directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The default branch name, or "main" if resolution fails.</returns>
    public static async Task<string> ResolveDefaultBranchAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var remoteHead = (await RunGitAsync(repoRoot, ["symbolic-ref", "refs/remotes/origin/HEAD", "--short"], true, cancellationToken)).Trim();
        if (remoteHead.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
        {
            return remoteHead["origin/".Length..];
        }

        return "main";
    }

    /// <summary>
    /// Executes a git command and returns whether it succeeded (exit code 0).
    /// </summary>
    /// <param name="repoRoot">Repository root directory.</param>
    /// <param name="args">Git command arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the command succeeded.</returns>
    public static async Task<bool> GitSucceedsAsync(string repoRoot, IReadOnlyList<string> args, CancellationToken cancellationToken)
        => (await RunGitProcessAsync(repoRoot, args, true, cancellationToken)) == 0;

    /// <summary>
    /// Executes a git command and returns its output.
    /// </summary>
    /// <param name="repoRoot">Repository root directory.</param>
    /// <param name="args">Git command arguments.</param>
    /// <param name="allowFailure">Whether to suppress exceptions on non-zero exit codes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command output.</returns>
    public static async Task<string> RunGitAsync(string repoRoot, IReadOnlyList<string> args, bool allowFailure, CancellationToken cancellationToken)
    {
        var (exitCode, output, error) = await RunGitProcessAsync(repoRoot, args, allowFailure, cancellationToken, captureOutput: true);
        if (exitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed with exit code {exitCode}: {error}");
        }

        return output;
    }

    /// <summary>
    /// Executes a git command and returns its exit code.
    /// </summary>
    /// <param name="repoRoot">Repository root directory.</param>
    /// <param name="args">Git command arguments.</param>
    /// <param name="allowFailure">Whether to suppress exceptions on non-zero exit codes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command exit code.</returns>
    public static async Task<int> RunGitProcessAsync(string repoRoot, IReadOnlyList<string> args, bool allowFailure, CancellationToken cancellationToken)
    {
        var (exitCode, _, error) = await RunGitProcessAsync(repoRoot, args, allowFailure, cancellationToken, captureOutput: false);
        if (exitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed with exit code {exitCode}: {error}");
        }

        return exitCode;
    }

    /// <summary>
    /// Executes a git command and returns exit code, output, and error streams.
    /// </summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunGitProcessAsync(string repoRoot, IReadOnlyList<string> args, bool allowFailure, CancellationToken cancellationToken, bool captureOutput)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, captureOutput ? output : string.Empty, error);
    }
}
