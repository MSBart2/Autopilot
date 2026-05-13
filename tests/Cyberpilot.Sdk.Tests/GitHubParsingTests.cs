using Cyberpilot.GitHub;

namespace Cyberpilot.Sdk.Tests;

public sealed class GitHubParsingTests
{
    [Fact]
    public void Split_HandlesLfOnlyOutputOnWindows()
    {
        var labels = LineSplitter.Split("sdk\nsdk/triage\nsdk/done\n");

        Assert.Equal(new[] { "sdk", "sdk/triage", "sdk/done" }, labels);
    }

    [Fact]
    public void Split_HandlesCrLfOutput()
    {
        var labels = LineSplitter.Split("sdk\r\nsdk/docs\r\n");

        Assert.Equal(new[] { "sdk", "sdk/docs" }, labels);
    }
}
