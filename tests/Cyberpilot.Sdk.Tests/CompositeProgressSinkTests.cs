using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class CompositeProgressSinkTests
{
    private static readonly StageDefinition TestStage = new("Test Stage", "test", "test.md", "sdk/test");
    private static readonly StageResult TestResult = StageResult.Empty;

    [Fact]
    public void OnStageStarted_DelegatesToAllSinks()
    {
        var sink1 = new RecordingSink();
        var sink2 = new RecordingSink();
        var composite = new CompositeProgressSink(sink1, sink2);

        composite.OnStageStarted(TestStage, 42);

        Assert.Single(sink1.StageStartedCalls);
        Assert.Single(sink2.StageStartedCalls);
        Assert.Equal((TestStage, 42), sink1.StageStartedCalls[0]);
        Assert.Equal((TestStage, 42), sink2.StageStartedCalls[0]);
    }

    [Fact]
    public void OnStageCompleted_DelegatesToAllSinks()
    {
        var sink1 = new RecordingSink();
        var sink2 = new RecordingSink();
        var composite = new CompositeProgressSink(sink1, sink2);

        composite.OnStageCompleted(TestStage, TestResult);

        Assert.Single(sink1.StageCompletedCalls);
        Assert.Single(sink2.StageCompletedCalls);
        Assert.Equal((TestStage, TestResult), sink1.StageCompletedCalls[0]);
        Assert.Equal((TestStage, TestResult), sink2.StageCompletedCalls[0]);
    }

    [Fact]
    public void OnMessage_DelegatesToAllSinks()
    {
        var sink1 = new RecordingSink();
        var sink2 = new RecordingSink();
        var composite = new CompositeProgressSink(sink1, sink2);

        composite.OnMessage("info", "hello");

        Assert.Single(sink1.MessageCalls);
        Assert.Single(sink2.MessageCalls);
        Assert.Equal(("info", "hello"), sink1.MessageCalls[0]);
        Assert.Equal(("info", "hello"), sink2.MessageCalls[0]);
    }

    [Fact]
    public void OnStreamDelta_DelegatesToAllSinks()
    {
        var sink1 = new RecordingSink();
        var sink2 = new RecordingSink();
        var composite = new CompositeProgressSink(sink1, sink2);

        composite.OnStreamDelta("chunk");

        Assert.Single(sink1.StreamDeltaCalls);
        Assert.Single(sink2.StreamDeltaCalls);
        Assert.Equal("chunk", sink1.StreamDeltaCalls[0]);
        Assert.Equal("chunk", sink2.StreamDeltaCalls[0]);
    }

    [Fact]
    public void OnBranchReady_DelegatesToAllSinks()
    {
        var sink1 = new RecordingSink();
        var sink2 = new RecordingSink();
        var composite = new CompositeProgressSink(sink1, sink2);

        composite.OnBranchReady("feature/my-branch");

        Assert.Single(sink1.BranchReadyCalls);
        Assert.Single(sink2.BranchReadyCalls);
        Assert.Equal("feature/my-branch", sink1.BranchReadyCalls[0]);
        Assert.Equal("feature/my-branch", sink2.BranchReadyCalls[0]);
    }

    [Fact]
    public void EmptySinks_DoesNotThrow()
    {
        var composite = new CompositeProgressSink();

        composite.OnStageStarted(TestStage, 1);
        composite.OnStageCompleted(TestStage, TestResult);
        composite.OnBranchReady("branch");
        composite.OnApprovalRequested(TestApprovalRequest());
        composite.OnMessage("info", "msg");
        composite.OnStreamDelta("delta");
        composite.OnDispatch("routing", "Test → GO");
    }

    [Fact]
    public void OnApprovalRequested_DelegatesToAllSinks()
    {
        var sink1 = new RecordingSink();
        var sink2 = new RecordingSink();
        var composite = new CompositeProgressSink(sink1, sink2);
        var request = TestApprovalRequest();

        composite.OnApprovalRequested(request);

        Assert.Single(sink1.ApprovalRequestedCalls);
        Assert.Single(sink2.ApprovalRequestedCalls);
        Assert.Same(request, sink1.ApprovalRequestedCalls[0]);
        Assert.Same(request, sink2.ApprovalRequestedCalls[0]);
    }

    [Fact]
    public void OnDispatch_DelegatesToAllSinks()
    {
        var sink1 = new RecordingSink();
        var sink2 = new RecordingSink();
        var composite = new CompositeProgressSink(sink1, sink2);

        composite.OnDispatch("routing", "Plan → GO");

        Assert.Single(sink1.DispatchCalls);
        Assert.Single(sink2.DispatchCalls);
        Assert.Equal(("routing", "Plan → GO"), sink1.DispatchCalls[0]);
        Assert.Equal(("routing", "Plan → GO"), sink2.DispatchCalls[0]);
    }

    private sealed class RecordingSink : ICyberpilotProgressSink
    {
        public List<(StageDefinition Stage, int IssueNumber)> StageStartedCalls { get; } = [];
        public List<(StageDefinition Stage, StageResult Result)> StageCompletedCalls { get; } = [];
        public List<string> BranchReadyCalls { get; } = [];
        public List<ApprovalGateRequest> ApprovalRequestedCalls { get; } = [];
        public List<(string Level, string Message)> MessageCalls { get; } = [];
        public List<string> StreamDeltaCalls { get; } = [];
        public List<(string Type, string Message)> DispatchCalls { get; } = [];

        public void OnStageStarted(StageDefinition stage, int issueNumber) =>
            StageStartedCalls.Add((stage, issueNumber));

        public void OnStageCompleted(StageDefinition stage, StageResult result) =>
            StageCompletedCalls.Add((stage, result));

        public void OnBranchReady(string branchName) =>
            BranchReadyCalls.Add(branchName);

        public void OnApprovalRequested(ApprovalGateRequest request) =>
            ApprovalRequestedCalls.Add(request);

        public void OnMessage(string level, string message) =>
            MessageCalls.Add((level, message));

        public void OnStreamDelta(string content) =>
            StreamDeltaCalls.Add(content);

        public void OnDispatch(string type, string message) =>
            DispatchCalls.Add((type, message));
    }

    private static ApprovalGateRequest TestApprovalRequest() => new(
        "approval-1",
        42,
        "plan",
        GateTiming.AfterStage,
        "Plan approval required.",
        "maintainer",
        "implement",
        DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
}
