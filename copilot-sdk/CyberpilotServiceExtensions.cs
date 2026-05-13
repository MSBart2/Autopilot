using Microsoft.Extensions.DependencyInjection;

namespace Cyberpilot;

/// <summary>
/// Registers Cyberpilot SDK services.
/// </summary>
public static class CyberpilotServiceExtensions
{
    /// <summary>
    /// Adds the default Cyberpilot runner implementation.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCyberpilotServices(this IServiceCollection services)
    {
        services.AddTransient<ICyberpilotRunner, CyberpilotRunner>();
        return services;
    }
}
