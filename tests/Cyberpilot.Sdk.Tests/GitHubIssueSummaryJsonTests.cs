using Cyberpilot.GitHub;

namespace Cyberpilot.Sdk.Tests;

public sealed class GitHubIssueSummaryJsonTests
{
    [Fact]
    public void ParseMany_EmptyString_ReturnsEmptyList()
    {
        var result = GitHubIssueSummaryJson.ParseMany("");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseMany_EmptyArray_ReturnsEmptyList()
    {
        var result = GitHubIssueSummaryJson.ParseMany("[]");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseMany_ParsesMultipleIssues()
    {
        var json = """
            [
                { "number": 1, "title": "First", "state": "open" },
                { "number": 2, "title": "Second", "state": "closed" }
            ]
            """;

        var result = GitHubIssueSummaryJson.ParseMany(json);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Number);
        Assert.Equal("First", result[0].Title);
        Assert.Equal(2, result[1].Number);
        Assert.Equal("Second", result[1].Title);
    }

    [Fact]
    public void ParseMany_ExtractsLabels()
    {
        var json = """
            [
                {
                    "number": 1,
                    "title": "Bug",
                    "labels": [{ "name": "bug" }, { "name": "priority" }]
                }
            ]
            """;

        var result = GitHubIssueSummaryJson.ParseMany(json);

        Assert.Equal(2, result[0].Labels.Count);
        Assert.Contains("bug", result[0].Labels);
        Assert.Contains("priority", result[0].Labels);
    }

    [Fact]
    public void ParseMany_DetectsActivePipelineLabel()
    {
        var json = """
            [
                {
                    "number": 1,
                    "title": "Triaged",
                    "labels": [{ "name": "sdk/triage" }]
                }
            ]
            """;

        var result = GitHubIssueSummaryJson.ParseMany(json);

        Assert.True(result[0].HasActivePipelineRun);
    }

    [Fact]
    public void ParseMany_NoActiveLabel()
    {
        var json = """
            [
                {
                    "number": 1,
                    "title": "Plain",
                    "labels": [{ "name": "bug" }]
                }
            ]
            """;

        var result = GitHubIssueSummaryJson.ParseMany(json);

        Assert.False(result[0].HasActivePipelineRun);
    }

    [Fact]
    public void ParseMany_HandlesSnakeCaseUpdatedAt()
    {
        var json = """
            [
                {
                    "number": 1,
                    "title": "Test",
                    "updated_at": "2024-06-15T10:30:00Z"
                }
            ]
            """;

        var result = GitHubIssueSummaryJson.ParseMany(json);

        Assert.Equal(new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero), result[0].UpdatedAt);
    }

    [Fact]
    public void ParseMany_HandlesCamelCaseUpdatedAt()
    {
        var json = """
            [
                {
                    "number": 1,
                    "title": "Test",
                    "updatedAt": "2024-06-15T10:30:00Z"
                }
            ]
            """;

        var result = GitHubIssueSummaryJson.ParseMany(json);

        Assert.Equal(new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero), result[0].UpdatedAt);
    }

    [Fact]
    public void ParseMany_MissingFields_UsesDefaults()
    {
        var json = """[{ "number": 42 }]""";

        var result = GitHubIssueSummaryJson.ParseMany(json);

        Assert.Equal(42, result[0].Number);
        Assert.Equal("", result[0].Title);
        Assert.Equal("", result[0].Url);
        Assert.Empty(result[0].Labels);
        Assert.Equal("OPEN", result[0].State);
        Assert.False(result[0].HasActivePipelineRun);
    }
}
