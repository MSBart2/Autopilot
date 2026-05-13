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

        var prompt = await builder.BuildAsync(new StageDefinition("TRIAGE", "triage", "triage.agent.md", "sdk/triage"), "classify issue");

        Assert.Contains("agent instructions", prompt);
        Assert.Contains($"Repository root: {targetRepo.Path}", prompt);
        Assert.Contains($"Agent prompt root: {agentRepo.Path}", prompt);
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

        var prompt = await builder.BuildAsync(new StageDefinition("PLAN", "plan", "plan.agent.md", "sdk/planning"), "plan issue");

        Assert.Contains("plan instructions", prompt);
        Assert.False(Directory.Exists(Path.Combine(targetRepo.Path, ".github", "agents")));
    }

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