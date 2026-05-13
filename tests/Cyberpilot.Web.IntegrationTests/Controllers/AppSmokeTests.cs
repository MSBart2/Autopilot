using Cyberpilot.Web.IntegrationTests.Fixtures;

namespace Cyberpilot.Web.IntegrationTests.Controllers;

[Collection("Integration")]
public class AppSmokeTests
{
    private readonly HttpClient _client;

    public AppSmokeTests(CyberpilotWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Home_ReturnsPipelinePortal()
    {
        var response = await _client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Mission Control");
    }

    [Fact]
    public async Task Pipelines_ReturnsPipelineDashboard()
    {
        var response = await _client.GetAsync("/Pipelines");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Orbital Command");
        content.Should().Contain("Run ledger");
    }

    [Fact]
    public async Task PipelineGuide_ReturnsMarkdown()
    {
        var response = await _client.GetAsync("/Pipelines/Guide/local");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("AI-Driven Software Development Lifecycle");
    }

    [Fact]
    public async Task Ready_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}