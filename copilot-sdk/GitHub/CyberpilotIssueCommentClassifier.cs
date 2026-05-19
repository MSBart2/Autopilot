namespace Cyberpilot.GitHub;

/// <summary>
/// Identifies GitHub issue comments that were produced by Cyberpilot pipeline stages.
/// </summary>
public static class CyberpilotIssueCommentClassifier
{
    private static readonly string[] AgentCommentMarkers =
    [
        "## 🕵️ Case File",
        "## 🎯 The Playbook",
        "## 🚀 Mission Control — Landing Report",
        "## ⚡ BUILD STARTED",
        "## ⚡ BUILD VALIDATED",
        "## ⚡ BUILD COMPLETE",
        "## 🎸 Review Started",
        "## 🎸 Review Complete",
        "## 📚 Docs & Verification",
        "SDK Cyberpilot branch ready:",
        "Planning Started",
        "Research Complete",
        "Branch Ready",
        "build-complete",
        "human verification",
    ];

    /// <summary>
    /// Returns whether a comment body appears to be a Cyberpilot-generated issue comment.
    /// </summary>
    /// <param name="body">The issue comment body.</param>
    /// <returns><see langword="true" /> when the body contains a known Cyberpilot marker.</returns>
    public static bool IsAgentComment(string? body)
        => !string.IsNullOrWhiteSpace(body)
            && AgentCommentMarkers.Any(marker => body.Contains(marker, StringComparison.OrdinalIgnoreCase));
}