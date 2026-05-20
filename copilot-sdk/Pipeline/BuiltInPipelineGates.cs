using Cyberpilot.Copilot;
using Cyberpilot.Git;
using Cyberpilot.GitHub;

namespace Cyberpilot.Pipeline;

internal static class BuiltInPipelineGates
{
    public const string RepositoryClean = "repository-clean";
    public const string ModelAvailable = "model-available";
    public const string RequiredLabels = "required-labels";
    public const string PullRequestPresent = "pull-request-present";
    public const string ReviewApproved = "review-approved";
    public const string BranchReady = "branch-ready";

    public static IReadOnlyDictionary<string, IPipelineGate> Create(
        IRepositoryCleanlinessChecker cleanlinessChecker,
        IModelAvailabilityChecker modelChecker,
        ISdkLabelService labels,
        IGitHubIssueClient issueClient)
        => new Dictionary<string, IPipelineGate>(StringComparer.OrdinalIgnoreCase)
        {
            [RepositoryClean] = new RepositoryCleanlinessGate(cleanlinessChecker),
            [ModelAvailable] = new ModelAvailabilityGate(modelChecker),
            [RequiredLabels] = new RequiredLabelsGate(labels),
            [PullRequestPresent] = new PullRequestPresenceGate(issueClient),
            [ReviewApproved] = new ReviewApprovalGate(),
            [BranchReady] = new BranchReadyGate(),
        };
}
