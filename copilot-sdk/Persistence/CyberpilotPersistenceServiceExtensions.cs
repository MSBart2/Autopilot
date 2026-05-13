using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cyberpilot.Persistence;

/// <summary>
/// Registers Cyberpilot persistence services.
/// </summary>
public static class CyberpilotPersistenceServiceExtensions
{
    /// <summary>
    /// Adds the shared Cyberpilot run-history database context.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="connectionString">The database connection string selected by the host.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCyberpilotPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CyberpilotDbContext>(options => options.UseSqlite(connectionString));
        return services;
    }
}
