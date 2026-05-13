using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.UnitTests.Models;

public sealed class ErrorViewModelTests
{
    [Fact]
    public void ShowRequestId_WhenRequestIdIsSet_ReturnsTrue()
    {
        var model = new ErrorViewModel { RequestId = "abc-123" };

        Assert.True(model.ShowRequestId);
    }

    [Fact]
    public void ShowRequestId_WhenRequestIdIsNull_ReturnsFalse()
    {
        var model = new ErrorViewModel { RequestId = null };

        Assert.False(model.ShowRequestId);
    }

    [Fact]
    public void ShowRequestId_WhenRequestIdIsEmpty_ReturnsFalse()
    {
        var model = new ErrorViewModel { RequestId = string.Empty };

        Assert.False(model.ShowRequestId);
    }
}
