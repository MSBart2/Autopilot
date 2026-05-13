using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class ApprovalGateRequestTests
{
    [Fact]
    public void NewRequest_IsPending()
    {
        var request = Request();

        Assert.True(request.IsPending);
        Assert.Equal(ApprovalStatus.Pending, request.Status);
        Assert.Null(request.Decision);
    }

    [Fact]
    public void Approve_ReturnsApprovedRequestWithTrimmedDecision()
    {
        var decidedAt = DateTimeOffset.Parse("2026-05-13T10:15:00Z");

        var approved = Request().Approve("  alice  ", "  ship it  ", decidedAt);

        Assert.False(approved.IsPending);
        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal(ApprovalStatus.Approved, approved.Decision?.Status);
        Assert.Equal("alice", approved.Decision?.DecidedBy);
        Assert.Equal("ship it", approved.Decision?.Reason);
        Assert.Equal(decidedAt, approved.Decision?.DecidedAt);
    }

    [Fact]
    public void Reject_ReturnsRejectedRequestWithDecision()
    {
        var decidedAt = DateTimeOffset.Parse("2026-05-13T10:20:00Z");

        var rejected = Request().Reject("reviewer", null, decidedAt);

        Assert.Equal(ApprovalStatus.Rejected, rejected.Status);
        Assert.Equal(ApprovalStatus.Rejected, rejected.Decision?.Status);
        Assert.Equal("reviewer", rejected.Decision?.DecidedBy);
        Assert.Null(rejected.Decision?.Reason);
    }

    [Fact]
    public void Complete_AlreadyDecidedRequest_Throws()
    {
        var approved = Request().Approve("alice", null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => approved.Reject("bob", null, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Complete_MissingActor_Throws(string? decidedBy)
    {
        Assert.Throws<ArgumentException>(() => Request().Approve(decidedBy!, null, DateTimeOffset.UtcNow));
    }

    private static ApprovalGateRequest Request() => new(
        "approval-1",
        42,
        "review",
        GateTiming.AfterStage,
        "Human approval required before delivery.",
        "maintainer",
        "docs",
        DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
}
