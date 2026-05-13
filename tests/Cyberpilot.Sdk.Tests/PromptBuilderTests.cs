using Cyberpilot.Pipeline;

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

    private static PolicyProfile StandardPolicy() => new("standard", PolicyStrictness.Standard);

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