using System.Diagnostics;
using Cyberpilot.Git;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cyberpilot.Pipeline;

/// <summary>
/// Resets a pipeline run's GitHub issue to a clean state for replay or archival.
/// </summary>
public sealed class PipelineResetService(
    IGitHubIssueClient issueClient,
    CyberpilotDbContext? dbContext)
{
    /// <summary>
    /// Resets the issue to a clean SDK state, preserving the run record in the database.
    /// Removes SDK stage labels, deletes agent comments, closes any open PR, and deletes the feature branch.
    /// </summary>
    /// <param name="runId">The run identifier to benchmark-reset.</param>
    /// <param name="repoRoot">Local repository root for git operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary of what was reset.</returns>
    public async Task<PipelineResetResult> BenchmarkResetAsync(string runId, string repoRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = dbContext is not null
            ? await dbContext.PipelineRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            : null;

        var issueNumber = run?.IssueNumber ?? 0;
        var branchName = run?.BranchName;

        return await ResetCoreAsync(issueNumber, branchName, repoRoot, deleteRun: false, run, cancellationToken);
    }

    /// <summary>
    /// Resets the issue to a clean SDK state and removes the run record from the database.
    /// Removes SDK stage labels, deletes agent comments, and deletes the feature branch.
    /// </summary>
    /// <param name="runId">The run identifier to reset and delete.</param>
    /// <param name="repoRoot">Local repository root for git operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary of what was reset.</returns>
    public async Task<PipelineResetResult> ResetMissionAsync(string runId, string repoRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = dbContext is not null
            ? await dbContext.PipelineRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            : null;

        var issueNumber = run?.IssueNumber ?? 0;
        var branchName = run?.BranchName;

        return await ResetCoreAsync(issueNumber, branchName, repoRoot, deleteRun: true, run, cancellationToken);
    }

    /// <summary>
    /// Resets an issue directly by number without requiring a run record.
    /// Useful from the CLI when no run ID is available.
    /// </summary>
    /// <param name="issueNumber">Issue number to reset.</param>
    /// <param name="repoRoot">Local repository root for git operations.</param>
    /// <param name="branchName">Optional explicit branch name. If null, branch cleanup is skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary of what was reset.</returns>
    public async Task<PipelineResetResult> ResetIssueAsync(int issueNumber, string repoRoot, string? branchName = null, CancellationToken cancellationToken = default)
    {
        return await ResetCoreAsync(issueNumber, branchName, repoRoot, deleteRun: false, run: null, cancellationToken);
    }

    private async Task<PipelineResetResult> ResetCoreAsync(
        int issueNumber,
        string? branchName,
        string repoRoot,
        bool deleteRun,
        PipelineRun? run,
        CancellationToken cancellationToken)
    {
        GitHubIssueSummary? issue = null;
        int deletedComments = 0;
        bool branchDeleted = false;
        bool prClosed = false;

        if (issueNumber > 0)
        {
            issue = await issueClient.GetIssueAsync(issueNumber, cancellationToken);

            // Close open PR if this is a benchmark reset
            if (!deleteRun)
            {
                var pr = await issueClient.FindPullRequestForIssueAsync(issueNumber, cancellationToken);
                if (pr is not null)
                {
                    await issueClient.ClosePullRequestAsync(pr.Number, cancellationToken);
                    prClosed = true;
                }
            }

            await ResetIssueLabelsAsync(issueNumber, cancellationToken);
            deletedComments = await DeleteAgentCommentsAsync(issueNumber, cancellationToken);

            if (string.IsNullOrWhiteSpace(branchName))
            {
                branchName = BranchProvisioner.CreateBranchName(issueNumber, issue?.Title ?? $"issue-{issueNumber}");
            }
        }

        if (!string.IsNullOrWhiteSpace(branchName))
        {
            branchDeleted = await DeleteIssueBranchAsync(repoRoot, branchName, cancellationToken);
        }

        if (run is not null && dbContext is not null)
        {
            if (deleteRun)
            {
                dbContext.PipelineRuns.Remove(run);
            }
            else
            {
                run.BenchmarkResetAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new PipelineResetResult(
            IssueNumber: issueNumber,
            DeletedComments: deletedComments,
            BranchDeleted: branchDeleted,
            BranchName: branchName,
            PrClosed: prClosed,
            RunDeleted: deleteRun && run is not null);
    }

    private async Task ResetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken)
    {
        var labels = await issueClient.GetIssueLabelsAsync(issueNumber, cancellationToken);
        foreach (var label in labels.Where(label => label.StartsWith("sdk/", StringComparison.OrdinalIgnoreCase)))
        {
            await issueClient.RemoveIssueLabelAsync(issueNumber, label, cancellationToken);
        }

        if (!labels.Contains("sdk", StringComparer.OrdinalIgnoreCase))
        {
            await issueClient.AddIssueLabelAsync(issueNumber, "sdk", cancellationToken);
        }
    }

    private async Task<int> DeleteAgentCommentsAsync(int issueNumber, CancellationToken cancellationToken)
    {
        var comments = await issueClient.ListIssueCommentsAsync(issueNumber, cancellationToken);
        var deleted = 0;
        foreach (var comment in comments.Where(c => IsAgentComment(c.Body)))
        {
            await issueClient.DeleteIssueCommentAsync(comment.Id, cancellationToken);
            deleted++;
        }

        return deleted;
    }

    private static bool IsAgentComment(string body)
        => CyberpilotIssueCommentClassifier.IsAgentComment(body);

    private static async Task<bool> DeleteIssueBranchAsync(string repoRoot, string branchName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchName) || !Directory.Exists(repoRoot))
        {
            return false;
        }

        var deletedRemote = await GitSucceedsAsync(repoRoot, ["push", "origin", "--delete", branchName], cancellationToken);
        var currentBranch = (await RunGitOutputAsync(repoRoot, ["branch", "--show-current"], cancellationToken)).Trim();
        if (currentBranch.Equals(branchName, StringComparison.OrdinalIgnoreCase))
        {
            var defaultBranch = await ResolveDefaultBranchAsync(repoRoot, cancellationToken);
            await GitSucceedsAsync(repoRoot, ["switch", defaultBranch], cancellationToken);
        }

        var deletedLocal = await GitSucceedsAsync(repoRoot, ["branch", "-D", branchName], cancellationToken);
        return deletedRemote || deletedLocal;
    }

    private static async Task<string> ResolveDefaultBranchAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var remoteHead = (await RunGitOutputAsync(repoRoot, ["symbolic-ref", "refs/remotes/origin/HEAD", "--short"], cancellationToken)).Trim();
        return remoteHead.StartsWith("origin/", StringComparison.OrdinalIgnoreCase)
            ? remoteHead["origin/".Length..]
            : "main";
    }

    private static async Task<bool> GitSucceedsAsync(string repoRoot, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var exitCode = await RunGitProcessAsync(repoRoot, args, captureOutput: false, cancellationToken);
        return exitCode == 0;
    }

    private static async Task<string> RunGitOutputAsync(string repoRoot, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var startInfo = BuildGitStartInfo(repoRoot, args);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return output;
    }

    private static async Task<int> RunGitProcessAsync(string repoRoot, IReadOnlyList<string> args, bool captureOutput, CancellationToken cancellationToken)
    {
        var startInfo = BuildGitStartInfo(repoRoot, args);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");
        await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static ProcessStartInfo BuildGitStartInfo(string repoRoot, IReadOnlyList<string> args)
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

        return startInfo;
    }
}
