using Microsoft.AspNetCore.SignalR;

namespace Cyberpilot.Web.Hubs;

/// <summary>
/// Streams Cyberpilot pipeline progress to dashboard clients.
/// </summary>
public sealed class PipelineHub : Hub
{
    /// <summary>
    /// Joins the SignalR group for a specific pipeline run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>A task that completes when the connection joins the group.</returns>
    public Task JoinRun(string runId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId));
    }

    /// <summary>
    /// Gets the SignalR group name for a run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The group name.</returns>
    public static string GroupName(string runId) => $"pipeline-run:{runId}";
}
