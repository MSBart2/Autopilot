namespace Cyberpilot.GitHub;

internal interface ISdkLabelService
{
    Task EnsureProvenanceAsync(int issueNumber, CancellationToken cancellationToken = default);
    Task EnsureRequiredLabelsAsync(bool createMissing, CancellationToken cancellationToken = default);
    Task ClearStageAsync(int issueNumber, CancellationToken cancellationToken = default);
    Task SetStageAsync(int issueNumber, string stageLabel, CancellationToken cancellationToken = default);
}

internal sealed class SdkLabelService(IGitHubIssueClient issueClient, TextWriter output) : ISdkLabelService
{
    private const string ProvenanceLabel = "sdk";
    private const string StageLabelPrefix = "sdk/";

    public static IReadOnlyList<string> RequiredLabels { get; } =
    [
        "sdk",
        "sdk/triage",
        "sdk/planning",
        "sdk/implementing",
        "sdk/review",
        "sdk/docs",
        "sdk/delivering",
        "sdk/done",
        "sdk/failed",
    ];

    public async Task EnsureProvenanceAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        await issueClient.AddIssueLabelAsync(issueNumber, ProvenanceLabel, cancellationToken);
    }

    public async Task EnsureRequiredLabelsAsync(bool createMissing, CancellationToken cancellationToken = default)
    {
        var existingLabels = await issueClient.GetRepositoryLabelsAsync(cancellationToken);
        var missingLabels = RequiredLabels
            .Where(label => !existingLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missingLabels.Length == 0)
        {
            output.WriteLine("All SDK labels are present.");
            return;
        }

        if (!createMissing)
        {
            throw new InvalidOperationException($"Missing SDK labels: {string.Join(", ", missingLabels)}. Rerun with --ensure-labels to create them.");
        }

        foreach (var label in missingLabels)
        {
            await issueClient.CreateOrUpdateLabelAsync(label, LabelColor(label), LabelDescription(label), cancellationToken);
            output.WriteLine($"Created label {label}.");
        }
    }

    public async Task SetStageAsync(int issueNumber, string stageLabel, CancellationToken cancellationToken = default)
    {
        await ClearStageAsync(issueNumber, cancellationToken);
        await issueClient.AddIssueLabelAsync(issueNumber, stageLabel, cancellationToken);
    }

    public async Task ClearStageAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        var labels = await issueClient.GetIssueLabelsAsync(issueNumber, cancellationToken);
        foreach (var label in labels.Where(label => label.StartsWith(StageLabelPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            await issueClient.RemoveIssueLabelAsync(issueNumber, label, cancellationToken);
        }
    }

    private static string LabelColor(string label)
    {
        return label switch
        {
            "sdk/failed" => "d73a4a",
            "sdk/done" => "0e8a16",
            "sdk" => "5319e7",
            _ => "6f42c1",
        };
    }

    private static string LabelDescription(string label)
    {
        return label switch
        {
            "sdk" => "Issue handled by the Cyberpilot SDK runner",
            "sdk/failed" => "Cyberpilot SDK runner halted before completion",
            "sdk/done" => "Cyberpilot SDK runner completed",
            _ => $"Cyberpilot SDK runner stage: {label[4..]}",
        };
    }
}
