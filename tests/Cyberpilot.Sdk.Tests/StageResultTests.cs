using Cyberpilot.Pipeline;

namespace Cyberpilot.Sdk.Tests;

public sealed class StageResultTests
{
    [Fact]
    public void Parse_UsesLastFencedJsonBlock()
    {
        var result = StageResult.Parse("""
			```json
			{ "status": "STOP" }
			```
			Later result:
			```json
			{ "status": "GO", "decision": "APPROVED" }
			```
			""");

        Assert.True(result.IsValid);
        Assert.Equal("GO", result.Status);
        Assert.Equal("approved", result.Decision);
    }

    [Fact]
    public void Parse_ReturnsInvalidWhenJsonBlockIsMissing()
    {
        var result = StageResult.Parse("No structured result here.");

        Assert.False(result.IsValid);
        Assert.Equal("INVALID", result.Status);
        Assert.Contains("No fenced JSON", result.Error);
    }

    [Fact]
    public void Parse_ReturnsInvalidForUnknownDecision()
    {
        var result = StageResult.Parse("""
			```json
			{ "status": "GO", "decision": "merge_now" }
			```
			""");

        Assert.False(result.IsValid);
        Assert.Contains("Unknown decision", result.Error);
    }

    [Fact]
    public void Parse_GoStatus_ReturnsGo()
    {
        var result = StageResult.Parse("""
            text before
            ```json
            {"status":"GO"}
            ```
            more text
            """);

        Assert.True(result.IsValid);
        Assert.Equal("GO", result.Status);
    }

    [Fact]
    public void Parse_StopStatus_ReturnsStop()
    {
        var result = StageResult.Parse("""
            ```json
            {"status":"STOP"}
            ```
            """);

        Assert.True(result.IsValid);
        Assert.Equal("STOP", result.Status);
    }

    [Fact]
    public void Parse_DuplicateStatus_ReturnsDuplicate()
    {
        var result = StageResult.Parse("""
            ```json
            {"status":"DUPLICATE"}
            ```
            """);

        Assert.True(result.IsValid);
        Assert.Equal("DUPLICATE", result.Status);
    }

    [Fact]
    public void Parse_ApprovedDecision()
    {
        var result = StageResult.Parse("""
            ```json
            {"status":"GO","decision":"approved"}
            ```
            """);

        Assert.True(result.IsValid);
        Assert.Equal("approved", result.Decision);
    }

    [Fact]
    public void Parse_ChangesRequestedDecision()
    {
        var result = StageResult.Parse("""
            ```json
            {"status":"GO","decision":"changes_requested"}
            ```
            """);

        Assert.True(result.IsValid);
        Assert.Equal("changes_requested", result.Decision);
    }

    [Fact]
    public void Parse_CommentDecision()
    {
        var result = StageResult.Parse("""
            ```json
            {"status":"GO","decision":"comment"}
            ```
            """);

        Assert.True(result.IsValid);
        Assert.Equal("comment", result.Decision);
    }

    [Fact]
    public void Parse_CaseInsensitiveStatus()
    {
        var result = StageResult.Parse("""
            ```json
            {"status":"go"}
            ```
            """);

        Assert.True(result.IsValid);
        Assert.Equal("GO", result.Status);
    }

    [Fact]
    public void Parse_MissingStatus_ReturnsInvalid()
    {
        var result = StageResult.Parse("""
            ```json
            {"decision":"approved"}
            ```
            """);

        Assert.False(result.IsValid);
        Assert.Contains("missing", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsInvalid()
    {
        var result = StageResult.Parse("""
            ```json
            {not valid json}
            ```
            """);

        Assert.False(result.IsValid);
        Assert.Contains("Malformed JSON", result.Error);
    }

    [Fact]
    public void Empty_HasCorrectDefaults()
    {
        var result = StageResult.Empty;

        Assert.Equal("GO", result.Status);
        Assert.Equal("unknown", result.Decision);
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public void InputTokens_DefaultsToNull()
    {
        Assert.Null(StageResult.Empty.InputTokens);
    }

    [Fact]
    public void OutputTokens_DefaultsToNull()
    {
        Assert.Null(StageResult.Empty.OutputTokens);
    }

    [Fact]
    public void WithExpression_SetsTokens()
    {
        var result = StageResult.Empty with { InputTokens = 500, OutputTokens = 1200 };

        Assert.Equal(500, result.InputTokens);
        Assert.Equal(1200, result.OutputTokens);
    }
}
