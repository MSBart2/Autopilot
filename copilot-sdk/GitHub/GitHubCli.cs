using System.Diagnostics;

namespace Cyberpilot.GitHub;

internal interface IGitHubCli
{
    Task<string> RunAsync(IReadOnlyList<string> args, bool allowFailure = false, CancellationToken cancellationToken = default);
}

internal sealed class GitHubCli(string repoRoot, string? repository) : IGitHubCli
{
    public async Task<string> RunAsync(IReadOnlyList<string> args, bool allowFailure = false, CancellationToken cancellationToken = default)
    {
        var allArgs = new List<string>(args);
        if (!string.IsNullOrWhiteSpace(repository))
        {
            allArgs.Add("--repo");
            allArgs.Add(repository);
        }

        var startInfo = new ProcessStartInfo("gh")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in allArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start gh process.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"gh {string.Join(' ', allArgs)} failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }
}
