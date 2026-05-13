using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.Controllers;

/// <summary>
/// Provides the Cyberpilot pipeline portal and error pages.
/// </summary>
[Route("[controller]/[action]")]
public class HomeController : Controller
{
    /// <summary>
    /// Displays the Cyberpilot pipeline overview.
    /// </summary>
    /// <returns>The default Index view.</returns>
    [HttpGet("/")]
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Displays the error page with the current request identifier, if available.
    /// </summary>
    /// <returns>The Error view with <see cref="ErrorViewModel"/>.</returns>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
