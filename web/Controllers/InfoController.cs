using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Cyberpilot.Web.Controllers;

/// <summary>
/// Provides usage, architecture, and configuration reference pages.
/// </summary>
[Route("[controller]/[action]")]
public class InfoController : Controller
{
    private readonly CyberpilotWebOptions _options;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Initializes a new instance of the <see cref="InfoController"/> class.
    /// </summary>
    /// <param name="options">The current Cyberpilot configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    public InfoController(IOptions<CyberpilotWebOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    /// <summary>
    /// Displays the operator usage guide.
    /// </summary>
    /// <returns>The Usage view.</returns>
    [HttpGet]
    public IActionResult Usage() => View();

    /// <summary>
    /// Displays the system architecture reference.
    /// </summary>
    /// <returns>The Architecture view.</returns>
    [HttpGet]
    public IActionResult Architecture() => View();

    /// <summary>
    /// Displays the current configuration and admin reference.
    /// </summary>
    /// <returns>The Admin view with the current configuration.</returns>
    [HttpGet]
    public IActionResult Admin() => View(new AdminViewModel(_options, _environment.EnvironmentName));
}
