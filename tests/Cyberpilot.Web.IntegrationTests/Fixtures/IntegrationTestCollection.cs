namespace Cyberpilot.Web.IntegrationTests.Fixtures;

/// <summary>
/// Defines a shared test collection so all integration tests reuse the same
/// <see cref="CyberpilotWebApplicationFactory"/> instance, improving test performance.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<CyberpilotWebApplicationFactory>
{
}
