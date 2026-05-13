using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class PipelinePauseDecisionTests
{
    [Fact]
    public void Continue_ReturnsNonPausingDecision()
    {
        var decision = PipelinePauseDecision.Continue();

        Assert.False(decision.ShouldPause);
        Assert.Equal(string.Empty, decision.Reason);
        Assert.Null(decision.ApprovalRequest);
    }

    [Fact]
    public void Pause_TrimsReasonAndCarriesApprovalRequest()
    {
        var approval = new ApprovalGateRequest(
            "approval-1",
            42,
            "review",
            GateTiming.AfterStage,
            "Review approval required.",
            "maintainer",
            "docs",
            DateTimeOffset.Parse("2026-05-13T10:00:00Z"));

        var decision = PipelinePauseDecision.Pause("  Waiting on maintainer.  ", approval);

        Assert.True(decision.ShouldPause);
        Assert.Equal("Waiting on maintainer.", decision.Reason);
        Assert.Same(approval, decision.ApprovalRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Pause_BlankReason_UsesDefaultReason(string? reason)
    {
        var decision = PipelinePauseDecision.Pause(reason!);

        Assert.True(decision.ShouldPause);
        Assert.Equal("Pipeline pause requested.", decision.Reason);
    }
}
