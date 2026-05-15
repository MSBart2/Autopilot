using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Cyberpilot.Git;

/// <summary>
/// Provisions deterministic local issue branches with idempotent reuse.
/// </summary>
public sealed partial class BranchProvisioner : IBranchProvisioner
{
    /// <inheritdoc />
    public async Task<CyberpilotBranchInfo> EnsureBranchAsync(string repository, int issueNumber, string issueTitle, string repoRoot, CancellationToken cancellationToken = default)
    {
        var branchName = CreateBranchName(issueNumber, issueTitle);
        var localExists = await GitSucceedsAsync(repoRoot, ["rev-parse", "--verify", branchName], cancellationToken);
        var remoteExists = await GitSucceedsAsync(repoRoot, ["ls-remote", "--exit-code", "--heads", "origin", branchName], cancellationToken);

        if (!localExists)
        {
            if (remoteExists)
            {
                await RunGitAsync(repoRoot, ["fetch", "origin", $"{branchName}:{branchName}"], cancellationToken: cancellationToken);
            }
            else
            {
                await RunGitAsync(repoRoot, ["branch", branchName], cancellationToken: cancellationToken);
            }
        }

        await RunGitAsync(repoRoot, ["switch", branchName], cancellationToken: cancellationToken);
        return new CyberpilotBranchInfo(branchName, !localExists && !remoteExists, remoteExists, null);
    }

    /// <summary>
    /// Creates a deterministic issue branch name.
    /// </summary>
    /// <param name="issueNumber">The GitHub issue number.</param>
    /// <param name="issueTitle">The issue title.</param>
    /// <returns>The branch name.</returns>
    public static string CreateBranchName(int issueNumber, string issueTitle)
    {
        ArgumentNullException.ThrowIfNull(issueTitle);
        var slug = SlugRegex().Replace(issueTitle.ToLowerInvariant(), "-").Trim('-'); // CA1308: ToLowerInvariant is correct for git branch slug generation
        if (slug.Length == 0)
        {
            slug = "work";
        }

        if (slug.Length > 48)
        {
            slug = slug[..48].Trim('-');
        }

        return $"sdk/issue-{issueNumber}-{slug}";
    }

    private static async Task<bool> GitSucceedsAsync(string repoRoot, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repoRoot, args, allowFailure: true, cancellationToken);
        return result == 0;
    }

    private static async Task<int> RunGitAsync(string repoRoot, IReadOnlyList<string> args, bool allowFailure = false, CancellationToken cancellationToken = default)
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
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed with exit code {process.ExitCode}: {error}");
        }

        return process.ExitCode;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
