using Cyberpilot.Web.Services;

namespace Cyberpilot.Web.Models;

/// <summary>
/// View model for the Admin configuration page.
/// </summary>
/// <param name="Options">The current loaded Cyberpilot configuration.</param>
/// <param name="EnvironmentName">The ASP.NET Core hosting environment name.</param>
public sealed record AdminViewModel(CyberpilotWebOptions Options, string EnvironmentName);
