using Microsoft.Extensions.DependencyInjection;

namespace Cyberpilot.Sdk.Tests;

public sealed class CyberpilotServiceExtensionsTests
{
    [Fact]
    public void AddCyberpilotServices_RegistersICyberpilotRunner()
    {
        var services = new ServiceCollection();
        services.AddCyberpilotServices();
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ICyberpilotRunner));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }
}
