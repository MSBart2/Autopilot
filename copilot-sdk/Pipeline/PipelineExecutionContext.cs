using Cyberpilot.Options;

namespace Cyberpilot.Pipeline;

internal sealed class PipelineExecutionContext(CyberpilotOptions options, PipelineDefinition definition)
{
    public CyberpilotOptions Options { get; } = options;

    public PipelineDefinition Definition { get; } = definition;

    public string FinalStage { get; set; } = "not-started";

    public string? BranchName { get; set; }

    public string? PrUrl { get; set; }

    public List<StageResult> StageResults { get; } = [];
}