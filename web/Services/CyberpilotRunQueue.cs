using System.Threading.Channels;

namespace Cyberpilot.Web.Services;

/// <summary>
/// Channel-backed queue for Cyberpilot runs.
/// </summary>
public sealed class CyberpilotRunQueue : ICyberpilotRunQueue
{
    private readonly Channel<WebPipelineRunRequest> channel = Channel.CreateBounded<WebPipelineRunRequest>(new BoundedChannelOptions(50)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    /// <inheritdoc />
    public ValueTask EnqueueAsync(WebPipelineRunRequest request, CancellationToken cancellationToken = default)
    {
        return channel.Writer.WriteAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<WebPipelineRunRequest> DequeueAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAsync(cancellationToken);
    }
}
