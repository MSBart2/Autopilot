using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cyberpilot.Persistence;

/// <summary>
/// Design-time factory for <see cref="CyberpilotDbContext"/> used by EF Core tooling.
/// </summary>
internal sealed class CyberpilotDbContextFactory : IDesignTimeDbContextFactory<CyberpilotDbContext>
{
    /// <inheritdoc />
    public CyberpilotDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CyberpilotDbContext>();
        optionsBuilder.UseSqlite("DataSource=cyberpilot-design.db");
        return new CyberpilotDbContext(optionsBuilder.Options);
    }
}
