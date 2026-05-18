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
    public void Parse_FinalMalformedJson_DoesNotFallBackToEarlierValidBlock()
    {
        var result = StageResult.Parse("""
            ```json
            {"status":"GO"}
            ```

            ```json
            {"status":
            ```
            """);

        Assert.False(result.IsValid);
        Assert.Equal("INVALID", result.Status);
        Assert.Contains("Malformed JSON", result.Error);
    }

    [Fact]
    public void Parse_ArtifactValueWithEmbeddedMarkdownFence_ReturnsArtifact()
    {
        var result = StageResult.Parse("""
            ```json
            {
              "status": "GO",
              "artifacts": {
                "triage-comment": "## Case File\n```markdown\n# Handoff\n- Evidence: solid\n```\nCase closed."
              }
            }
            ```
            """);

        Assert.True(result.IsValid);
        var artifact = Assert.Single(result.Artifacts!);
        Assert.Equal("triage-comment", artifact.Name);
        Assert.Contains("```markdown", artifact.Value);
        Assert.Contains("Case closed.", artifact.Value);
    }

    [Fact]
    public void Parse_ArtifactValueWithBracesInsideString_ReturnsArtifact()
    {
        var result = StageResult.Parse("""
            ```json
            {
              "status": "GO",
              "artifacts": {
                "plan-comment": "Touch Program.cs only if config contains {FeatureFlags: {Weather: true}}."
              }
            }
            ```
            """);

        Assert.True(result.IsValid);
        var artifact = Assert.Single(result.Artifacts!);
        Assert.Equal("plan-comment", artifact.Name);
        Assert.Contains("{FeatureFlags", artifact.Value);
    }

    [Fact]
    public void Parse_TrailingTextAfterBalancedObjectInFinalFence_IgnoresTrailingText()
    {
        var result = StageResult.Parse("""
            ```json
            {"status":"GO", "decision":"approved"}
            Markdown that should not be here but should not break the balanced object repair.
            ```
            """);

        Assert.True(result.IsValid);
        Assert.Equal("GO", result.Status);
        Assert.Equal("approved", result.Decision);
    }

    [Fact]
    public void Empty_HasCorrectDefaults()
    {
        var result = StageResult.Empty;

        Assert.Equal("GO", result.Status);
        Assert.Equal("unknown", result.Decision);
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Equal(PipelineDefinitionDefaults.ContractVersion, result.ContractVersion);
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
    public void Metrics_DefaultsToNull()
    {
        Assert.Null(StageResult.Empty.Metrics);
    }

    [Fact]
    public void WithExpression_SetsTokens()
    {
        var result = StageResult.Empty with { InputTokens = 500, OutputTokens = 1200 };

        Assert.Equal(500, result.InputTokens);
        Assert.Equal(1200, result.OutputTokens);
    }

    [Fact]
    public void Parse_WithContractArtifactsEvidenceAndActions_ReturnsStructuredFields()
    {
        var result = StageResult.Parse("""
                        ```json
                        {
                            "status": "STOP",
                            "decision": "changes_requested",
                            "contract_version": "1.1",
                            "artifacts": [
                                { "name": "validation-summary", "summary": "Tests failed", "uri": "file://validation.md" }
                            ],
                            "evidence": [
                                { "name": "build", "summary": "dotnet build failed", "uri": "log://build" }
                            ],
                            "policy_rationale": "Strict profile requires a green build.",
                            "required_actions": ["Fix the failing build", "Rerun validation"]
                        }
                        ```
                        """);

        Assert.True(result.IsValid);
        Assert.Equal("1.1", result.ContractVersion);
        var artifact = Assert.Single(result.Artifacts!);
        Assert.Equal("validation-summary", artifact.Name);
        Assert.Equal("Tests failed", artifact.Value);
        Assert.Equal("file://validation.md", artifact.Uri);
        var evidence = Assert.Single(result.Evidence!);
        Assert.Equal("build", evidence.Name);
        Assert.Equal("dotnet build failed", evidence.Summary);
        Assert.Equal("log://build", evidence.Uri);
        Assert.Equal("Strict profile requires a green build.", result.PolicyRationale);
        Assert.Equal(["Fix the failing build", "Rerun validation"], result.RequiredActions);
    }

    [Fact]
    public void Parse_WithArtifactObject_ReturnsArtifactEntries()
    {
        var result = StageResult.Parse("""
                        ```json
                        {
                            "status": "GO",
                            "artifacts": {
                                "plan-comment": "Posted implementation plan",
                                "branch": "cyberpilot/issue-42"
                            }
                        }
                        ```
                        """);

        Assert.True(result.IsValid);
        Assert.Collection(
                result.Artifacts!,
                artifact =>
                {
                    Assert.Equal("plan-comment", artifact.Name);
                    Assert.Equal("Posted implementation plan", artifact.Value);
                },
                artifact =>
                {
                    Assert.Equal("branch", artifact.Name);
                    Assert.Equal("cyberpilot/issue-42", artifact.Value);
                });
    }
}
