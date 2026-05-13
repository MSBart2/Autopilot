namespace Cyberpilot.Web.Services;

/// <summary>
/// Queues Cyberpilot pipeline runs for background execution.
/// </summary>
public interface ICyberpilotRunQueue
{
    /// <summary>
    /// Enqueues a run request.
    /// </summary>
    /// <param name="request">The run request.</param>
    /// <param name="cancellationToken">A token that cancels enqueueing.</param>
    /// <returns>A task that completes when the request is queued.</returns>
    ValueTask EnqueueAsync(WebPipelineRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues the next run request.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels dequeueing.</param>
    /// <returns>The next run request.</returns>
    ValueTask<WebPipelineRunRequest> DequeueAsync(CancellationToken cancellationToken);
}
