using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class TextWriterProgressSinkTests
{
    private static readonly StageDefinition TestStage = new("Triage", "triage", "triage.md", "sdk/triage");

    [Fact]
    public void OnStageStarted_WritesFormattedHeader()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var sink = new TextWriterProgressSink(output, error);

        sink.OnStageStarted(TestStage, 99);

        var text = output.ToString();
        Assert.Contains("Triage", text);
        Assert.Contains("#99", text);
        Assert.Contains("sdk/triage", text);
        Assert.Contains("============", text);
    }

    [Fact]
    public void OnStageCompleted_WritesStatusAndDecision()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var sink = new TextWriterProgressSink(output, error);
        var result = new StageResult("GO", "approved", true, null);

        sink.OnStageCompleted(TestStage, result);

        var text = output.ToString();
        Assert.Contains("GO", text);
        Assert.Contains("approved", text);
        Assert.Contains("Triage", text);
    }

    [Fact]
    public void OnMessage_InfoLevel_WritesToOutput()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var sink = new TextWriterProgressSink(output, error);

        sink.OnMessage("info", "all good");

        Assert.Contains("[info] all good", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void OnMessage_FailLevel_WritesToError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var sink = new TextWriterProgressSink(output, error);

        sink.OnMessage("fail", "something broke");

        Assert.Contains("[fail] something broke", error.ToString());
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void OnStreamDelta_WritesContentWithoutNewline()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var sink = new TextWriterProgressSink(output, error);

        sink.OnStreamDelta("hello");
        sink.OnStreamDelta(" world");

        Assert.Equal("hello world", output.ToString());
    }
}
