using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.UnitTests.Models;

public sealed class CyberpilotStatusTests
{
    [Fact]
    public void None_HasExpectedDefaults()
    {
        var status = CyberpilotStatus.None;

        Assert.Equal(CyberpilotRunnerType.None, status.Runner);
        Assert.Null(status.Stage);
        Assert.False(status.IsActive);
        Assert.False(status.IsDone);
        Assert.False(status.IsFailed);
    }

    [Fact]
    public void None_HasAny_ReturnsFalse()
    {
        Assert.False(CyberpilotStatus.None.HasAny);
    }

    [Fact]
    public void Cloud_HasAny_ReturnsTrue()
    {
        var status = new CyberpilotStatus(CyberpilotRunnerType.Cloud, "triage", true, false, false);

        Assert.True(status.HasAny);
    }

    [Fact]
    public void Cloud_RunnerLabel_ReturnsCloud()
    {
        var status = new CyberpilotStatus(CyberpilotRunnerType.Cloud, "triage", true, false, false);

        Assert.Equal("Cloud", status.RunnerLabel);
    }

    [Fact]
    public void Cloud_RunnerIcon_ReturnsCloudEmoji()
    {
        var status = new CyberpilotStatus(CyberpilotRunnerType.Cloud, "triage", true, false, false);

        Assert.Equal("☁️", status.RunnerIcon);
    }

    [Fact]
    public void Local_RunnerLabel_ReturnsLocal()
    {
        var status = new CyberpilotStatus(CyberpilotRunnerType.Local, "plan", true, false, false);

        Assert.Equal("Local", status.RunnerLabel);
    }

    [Fact]
    public void Local_RunnerIcon_ReturnsLaptopEmoji()
    {
        var status = new CyberpilotStatus(CyberpilotRunnerType.Local, "plan", true, false, false);

        Assert.Equal("💻", status.RunnerIcon);
    }

    [Fact]
    public void Sdk_RunnerLabel_ReturnsSdk()
    {
        var status = new CyberpilotStatus(CyberpilotRunnerType.Sdk, "implement", true, false, false);

        Assert.Equal("SDK", status.RunnerLabel);
    }

    [Fact]
    public void Sdk_RunnerIcon_ReturnsLightningEmoji()
    {
        var status = new CyberpilotStatus(CyberpilotRunnerType.Sdk, "implement", true, false, false);

        Assert.Equal("⚡", status.RunnerIcon);
    }

    [Fact]
    public void None_RunnerLabel_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CyberpilotStatus.None.RunnerLabel);
    }

    [Fact]
    public void None_RunnerIcon_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CyberpilotStatus.None.RunnerIcon);
    }
}
