using Cyberpilot.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Cyberpilot.Sdk.Tests;

public sealed class CyberpilotPersistenceServiceExtensionsTests
{
    [Fact]
    public void AddCyberpilotPersistence_RegistersDbContext()
    {
        var services = new ServiceCollection();
        services.AddCyberpilotPersistence("Data Source=:memory:");
        var sp = services.BuildServiceProvider();
        var dbContext = sp.GetService<CyberpilotDbContext>();
        Assert.NotNull(dbContext);
    }

    [Fact]
    public void AddCyberpilotPersistence_NullConnectionString_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddCyberpilotPersistence(null!));
    }

    [Fact]
    public void AddCyberpilotPersistence_EmptyConnectionString_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddCyberpilotPersistence(""));
    }
}
