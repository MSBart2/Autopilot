using Cyberpilot.Copilot;

namespace Cyberpilot.Sdk.Tests;

public sealed class ModelAvailabilityResultTests
{
    [Fact]
    public void Available_IsAvailableTrue()
    {
        Assert.True(ModelAvailabilityResult.Available.IsAvailable);
    }

    [Fact]
    public void Available_ErrorIsNull()
    {
        Assert.Null(ModelAvailabilityResult.Available.Error);
    }

    [Fact]
    public void Unavailable_IsAvailableFalse()
    {
        var result = ModelAvailabilityResult.Unavailable("Model not found");
        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Unavailable_PreservesError()
    {
        var result = ModelAvailabilityResult.Unavailable("Model xyz not available");
        Assert.Equal("Model xyz not available", result.Error);
    }
}
