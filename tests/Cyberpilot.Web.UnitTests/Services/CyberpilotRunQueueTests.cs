using Cyberpilot.Web.Services;

namespace Cyberpilot.Web.UnitTests.Services;

public sealed class CyberpilotRunQueueTests
{
    private static WebPipelineRunRequest CreateRequest(string runId = "run-1")
        => new(runId, 1, "owner/repo", "C:\\Repos\\Repo", "C:\\Repos\\Cyberpilot", null, "gpt-4.1", false, TimeSpan.FromMinutes(10), false);

    [Fact]
    public async Task EnqueueAndDequeue_ReturnsSameRequest()
    {
        var queue = new CyberpilotRunQueue();
        var request = CreateRequest();

        await queue.EnqueueAsync(request);
        var result = await queue.DequeueAsync(CancellationToken.None);

        Assert.Equal(request, result);
    }

    [Fact]
    public async Task Dequeue_BlocksUntilEnqueued()
    {
        var queue = new CyberpilotRunQueue();
        var request = CreateRequest();

        var dequeueTask = queue.DequeueAsync(CancellationToken.None);
        Assert.False(dequeueTask.IsCompleted);

        await queue.EnqueueAsync(request);
        var result = await dequeueTask;

        Assert.Equal(request, result);
    }

    [Fact]
    public async Task MultipleEnqueues_DequeueInFifoOrder()
    {
        var queue = new CyberpilotRunQueue();
        var request1 = CreateRequest("run-1");
        var request2 = CreateRequest("run-2");
        var request3 = CreateRequest("run-3");

        await queue.EnqueueAsync(request1);
        await queue.EnqueueAsync(request2);
        await queue.EnqueueAsync(request3);

        Assert.Equal(request1, await queue.DequeueAsync(CancellationToken.None));
        Assert.Equal(request2, await queue.DequeueAsync(CancellationToken.None));
        Assert.Equal(request3, await queue.DequeueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Dequeue_CancellationThrows()
    {
        var queue = new CyberpilotRunQueue();
        using var cts = new CancellationTokenSource();

        var dequeueTask = queue.DequeueAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await dequeueTask);
    }
}
