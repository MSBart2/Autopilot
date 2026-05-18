namespace Cyberpilot.Pipeline;

/// <summary>
/// Summarizes the outcome of a pipeline reset operation.
/// </summary>
/// <param name="IssueNumber">The GitHub issue number that was reset.</param>
/// <param name="DeletedComments">Number of agent comments removed from the issue.</param>
/// <param name="BranchDeleted">Whether the feature branch was successfully deleted.</param>
/// <param name="BranchName">The feature branch name that was targeted.</param>
/// <param name="PrClosed">Whether an open pull request was closed.</param>
/// <param name="RunDeleted">Whether the pipeline run record was removed from the database.</param>
public sealed record PipelineResetResult(
    int IssueNumber,
    int DeletedComments,
    bool BranchDeleted,
    string? BranchName,
    bool PrClosed,
    bool RunDeleted)
{
    /// <summary>
    /// Returns a human-readable summary of the reset outcome.
    /// </summary>
    public string ToSummary()
    {
        var parts = new List<string>();

        parts.Add($"Removed {DeletedComments} agent comment(s) and cleared SDK stage labels.");

        if (PrClosed)
            parts.Add("Closed open pull request.");

        if (BranchDeleted)
            parts.Add($"Deleted branch {BranchName}.");
        else if (!string.IsNullOrWhiteSpace(BranchName))
            parts.Add($"Branch {BranchName} was not found or could not be deleted.");

        if (RunDeleted)
            parts.Add("Run record removed from database.");
        else if (IssueNumber > 0)
            parts.Add("Run metrics preserved in database.");

        return string.Join(" ", parts);
    }
}
