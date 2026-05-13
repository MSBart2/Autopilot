namespace Cyberpilot.Web.Models;

/// <summary>
/// Represents data for displaying error information to the user.
/// </summary>
public class ErrorViewModel
{
    /// <summary>
    /// Gets or sets the unique request identifier for the current HTTP request.
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Gets a value indicating whether the <see cref="RequestId"/> is available for display.
    /// </summary>
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
