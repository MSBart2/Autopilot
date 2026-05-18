using System.Diagnostics;

namespace Cyberpilot.Git;

/// <summary>
/// Reads Git revision information from a local repository.
/// </summary>
public static class GitRevParser
{
    /// <summary>
    /// Returns the full HEAD commit SHA for the repository at <paramref name="repoRoot"/>,
    /// or <see langword="null"/> if git is unavailable or the directory is not a git repo.
    /// </summary>
    public static async Task<string?> TryGetHeadShaAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("rev-parse");
            startInfo.ArgumentList.Add("HEAD");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> to find the nearest ancestor
    /// directory containing a <c>.git</c> entry, or <see langword="null"/> if none is found.
    /// </summary>
    public static string? FindGitRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))
                || File.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
