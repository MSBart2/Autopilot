using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Cyberpilot.Web.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for integration testing the Cyberpilot application.
/// Sets the environment to "Testing" to avoid HTTPS redirect issues during tests.
/// </summary>
public class CyberpilotWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Configures the web host for testing purposes.
    /// </summary>
    /// <param name="builder">The web host builder to configure.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"cyberpilot-tests-{Guid.NewGuid():N}.db");
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CyberpilotDb"] = $"Data Source={databasePath}",
                ["GitHub:Token"] = string.Empty,
                ["Cyberpilot:Repository"] = "rbmathis/Cyberpilot",
                ["Cyberpilot:RepoRoot"] = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."))
            });
        });
    }
}
