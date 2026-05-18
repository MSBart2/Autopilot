using Cyberpilot.Pipeline;
using Cyberpilot.Options;

namespace Cyberpilot.Sdk.Tests;

public sealed class PromptBuilderTests
{
    [Fact]
    public async Task BuildAsync_ReadsStagePromptFromAgentPromptRoot()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "triage.agent.md"), "agent instructions");
        var builder = new PromptBuilder(targetRepo.Path, agentRepo.Path, 7);

        var prompt = await builder.BuildAsync(Stage("TRIAGE", "triage", "triage.agent.md", "sdk/triage", ["triage-comment"]), "classify issue", StandardPolicy());

        Assert.Contains("agent instructions", prompt);
        Assert.Contains($"Repository root: {targetRepo.Path}", prompt);
        Assert.Contains($"Agent prompt root: {agentRepo.Path}", prompt);
        Assert.Contains("Stage result contract version: 1.0", prompt);
        Assert.Contains("Required artifacts: `triage-comment`", prompt);
        Assert.Contains("\"contract_version\": \"1.0\"", prompt);
        Assert.Contains("\"triage-comment\": \"brief artifact summary or URI\"", prompt);
    }

    [Fact]
    public async Task BuildAsync_DoesNotRequireTargetRepoToContainAgentPrompts()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "plan.agent.md"), "plan instructions");
        var builder = new PromptBuilder(targetRepo.Path, agentRepo.Path, 12);

        var prompt = await builder.BuildAsync(Stage("PLAN", "plan", "plan.agent.md", "sdk/planning", []), "plan issue", StandardPolicy());

        Assert.Contains("plan instructions", prompt);
        Assert.False(Directory.Exists(Path.Combine(targetRepo.Path, ".github", "agents")));
    }

    [Fact]
    public async Task BuildAsync_IncludesPolicyRationaleAndRequiredActionsContract()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "implement.agent.md"), "implement instructions");
        var builder = new PromptBuilder(targetRepo.Path, agentRepo.Path, 21);

        var prompt = await builder.BuildAsync(
            Stage("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing", ["pull-request", "validation-summary"]),
            "implement issue",
            new PolicyProfile("strict", PolicyStrictness.Strict));

        Assert.Contains("Policy profile: strict", prompt);
        Assert.Contains("Required artifacts: `pull-request`, `validation-summary`", prompt);
        Assert.Contains("\"policy_rationale\": \"why this result satisfies the strict policy profile\"", prompt);
        Assert.Contains("\"required_actions\": []", prompt);
        Assert.Contains("When status is STOP", prompt);
    }

    [Fact]
    public async Task BuildAsync_WithWindowsCommandStyle_IncludesPowerShellGuidance()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "implement.agent.md"), "implement instructions");
        var builder = new PromptBuilder(
            targetRepo.Path,
            agentRepo.Path,
            21,
            runtimePreferences: new CyberpilotRuntimePreferences(CommandStylePreference.Windows));

        var prompt = await builder.BuildAsync(
            Stage("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing", []),
            "implement issue",
            StandardPolicy());

        Assert.Contains("This run prefers Windows/PowerShell-native command syntax", prompt);
        Assert.Contains("Select-Object -Last", prompt);
        Assert.Contains("instead of `| tail -n <n>`", prompt);
    }

    [Fact]
    public async Task BuildAsync_WithLinuxCommandStyle_IncludesPosixGuidance()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "implement.agent.md"), "implement instructions");
        var builder = new PromptBuilder(
            targetRepo.Path,
            agentRepo.Path,
            21,
            runtimePreferences: new CyberpilotRuntimePreferences(CommandStylePreference.Linux));

        var prompt = await builder.BuildAsync(
            Stage("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing", []),
            "implement issue",
            StandardPolicy());

        Assert.Contains("This run prefers Linux/POSIX shell command syntax", prompt);
        Assert.Contains("tail -n", prompt);
        Assert.Contains("Avoid PowerShell-specific cmdlets", prompt);
    }

    [Fact]
    public async Task BuildAsync_ForDeliverStage_IncludesLandingReportEvidenceGuidance()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "deliver.agent.md"), "deliver instructions");
        var builder = new PromptBuilder(targetRepo.Path, agentRepo.Path, 42);

        var prompt = await builder.BuildAsync(
            Stage("LAND", "deliver", "deliver.agent.md", "sdk/delivering", ["landing-report"]),
            "merge the approved PR and post the landing report",
            StandardPolicy());

        Assert.Contains("## Landing Report Evidence", prompt);
        Assert.Contains("include a compact evidence and policy summary", prompt);
        Assert.Contains("Link to the merged pull request or relevant PR evidence", prompt);
        Assert.Contains("Summarize policy signals, gate outcomes, approvals", prompt);
    }

    [Fact]
    public async Task BuildAsync_WithTargetRepositoryProfile_IncludesProfileContext()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "implement.agent.md"), "implement instructions");
        var builder = new PromptBuilder(
            targetRepo.Path,
            agentRepo.Path,
            42,
            "Repository profile detected: languages: .NET | build: dotnet build ./App.sln | test: dotnet test ./App.sln.");

        var prompt = await builder.BuildAsync(
            Stage("IMPLEMENT", "implement", "implement.agent.md", "sdk/implementing", ["validation-summary"]),
            "implement issue",
            StandardPolicy());

        Assert.Contains("## Target Repository Profile", prompt);
        Assert.Contains("Use this detected target-repository context", prompt);
        Assert.Contains("dotnet build ./App.sln", prompt);
        Assert.Contains("dotnet test ./App.sln", prompt);
    }

    [Fact]
    public async Task BuildAsync_WithExecutionContext_IncludesPrFirstReviewContext()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "pipeline-review.agent.md"), "review instructions");
        var builder = new PromptBuilder(targetRepo.Path, agentRepo.Path, 42);
        var context = Context(targetRepo.Path);
        context.BranchName = "cyberpilot/issue-42";
        context.PrUrl = "https://github.com/owner/repo/pull/123";
        context.RecordStageResult(
            "plan",
            new StageResult(
                "GO",
                "approved",
                true,
                null,
                Artifacts: [new StageArtifact("plan-comment", "Use the existing branch.")],
                Evidence: [new StageEvidence("plan", "Plan approved.")]));
        context.RecordStageResult(
            "implement",
            new StageResult(
                "GO",
                "approved",
                true,
                null,
                Artifacts: [new StageArtifact("pull-request", "PR #123 is ready.")],
                Evidence: [new StageEvidence("validation", "dotnet test passed.")]));

        var prompt = await builder.BuildAsync(
            Stage("REVIEW", "review", "pipeline-review.agent.md", "sdk/reviewing", ["review-verdict"]),
            "review the PR",
            StandardPolicy(),
            context);

        Assert.Contains("## Harness Context", prompt);
        Assert.Contains("- Issue: #42", prompt);
        Assert.Contains("- Repository: owner/repo", prompt);
        Assert.Contains("- Head branch: cyberpilot/issue-42", prompt);
        Assert.Contains("- Pull request: #123 at https://github.com/owner/repo/pull/123", prompt);
        Assert.Contains("- plan: GO / approved", prompt);
        Assert.Contains("artifact: pull-request: PR #123 is ready.", prompt);
        Assert.DoesNotContain("triage:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured artifacts as the primary workflow state", prompt);
    }

    [Fact]
    public async Task BuildAsync_ForTriageContext_PrunesBranchAndPullRequest()
    {
        using var targetRepo = new TempDirectory();
        using var agentRepo = new TempDirectory();
        var agentsDirectory = Path.Combine(agentRepo.Path, ".github", "agents");
        Directory.CreateDirectory(agentsDirectory);
        await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "triage.agent.md"), "triage instructions");
        var builder = new PromptBuilder(targetRepo.Path, agentRepo.Path, 42);
        var context = Context(targetRepo.Path);
        context.BranchName = "cyberpilot/issue-42";
        context.PrUrl = "https://github.com/owner/repo/pull/123";

        var prompt = await builder.BuildAsync(
            Stage("TRIAGE", "triage", "triage.agent.md", "sdk/triage", ["triage-comment"]),
            "triage issue",
            StandardPolicy(),
            context);

        Assert.Contains("## Harness Context", prompt);
        Assert.DoesNotContain("Head branch:", prompt);
        Assert.DoesNotContain("Pull request:", prompt);
    }

    private static PolicyProfile StandardPolicy() => new("standard", PolicyStrictness.Standard);

    private static PipelineExecutionContext Context(string repoRoot)
    {
        var options = new CyberpilotOptions(42, repoRoot, "owner/repo", "test-model", false, false, false, false, TimeSpan.FromMinutes(10), true, false, null, null, false);
        return new PipelineExecutionContext(options, DefaultPipelineDefinitionProvider.Definition);
    }

    private static PipelineStageDefinition Stage(string displayName, string name, string promptFile, string label, IReadOnlyList<string> requiredArtifacts)
        => new(new StageDefinition(displayName, name, promptFile, label), new StageContract(PipelineDefinitionDefaults.ContractVersion, requiredArtifacts), []);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
