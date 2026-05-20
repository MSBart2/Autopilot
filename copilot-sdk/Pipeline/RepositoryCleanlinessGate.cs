using Cyberpilot.Git;

namespace Cyberpilot.Pipeline;

/// <summary>
/// Gate that validates the repository has a clean working tree before pipeline execution.
/// </summary>
internal sealed class RepositoryCleanlinessGate(IRepositoryCleanlinessChecker cleanlinessChecker) : IPipelineGate
{
    public async Task<PipelineGateResult> EvaluateAsync(PipelineGateContext context, CancellationToken cancellationToken = default)
    {
        var repoRoot = context.ExecutionContext.Options.RepoRoot;
        var result = await cleanlinessChecker.CheckAsync(repoRoot, cancellationToken);
        
        if (result.IsClean)
        {
            return PipelineGateResult.Pass("Repository has a clean working tree.");
        }

        return PipelineGateResult.Fail(
            $"Repository has uncommitted changes:\n\n{result.Error}",
            isRetryable: true,
            requiredActions: [
                "Commit your changes: git add -A && git commit -m '<message>'",
                "Or stash your changes: git stash",
                "Then retry the pipeline."
            ]);
    }
}
