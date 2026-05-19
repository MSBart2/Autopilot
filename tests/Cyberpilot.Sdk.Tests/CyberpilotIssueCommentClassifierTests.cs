using Cyberpilot.GitHub;

namespace Cyberpilot.Sdk.Tests;

public class CyberpilotIssueCommentClassifierTests
{
    [Theory]
    [InlineData("## ⚡ BUILD COMPLETE — Ship It! 🔨\nDone.")]
    [InlineData("## 🎸 Review Complete\nVerdict posted.")]
    [InlineData("## 📚 Docs & Verification\nAll tidy.")]
    [InlineData("## 🚀 Mission Control — Landing Report\nTouchdown.")]
    public void IsAgentComment_RecognizesCurrentCyberpilotMarkers(string body)
    {
        Assert.True(CyberpilotIssueCommentClassifier.IsAgentComment(body));
    }

    [Fact]
    public void IsAgentComment_IgnoresHumanComment()
    {
        Assert.False(CyberpilotIssueCommentClassifier.IsAgentComment("Please take another look at the screenshot."));
    }
}
