using Cyberpilot;
using Cyberpilot.Persistence;
using Cyberpilot.Web.Hubs;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cyberpilot.Web.UnitTests.Services;

public sealed class CyberpilotPipelineServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");

    public CyberpilotPipelineServiceTests()
    {
        connection.Open();
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_AllowsDifferentRepositoryRootsToRunConcurrently()
    {
        var queue = new CyberpilotRunQueue();
        var runner = new BlockingRunner(expectedConcurrentStarts: 2);
        using var provider = CreateProvider(queue, runner);
        await SeedRunsAsync(provider, "run-1", "owner/repo-one", "run-2", "owner/repo-two");
        var service = CreateService(provider, queue);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(CreateRequest("run-1", "owner/repo-one", "C:\\Repos\\One"));
        await queue.EnqueueAsync(CreateRequest("run-2", "owner/repo-two", "C:\\Repos\\Two"));

        await runner.WaitForExpectedStartsAsync();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>();
            var runningCount = await dbContext.PipelineRuns.CountAsync(run => run.Status == "Running");
            Assert.Equal(2, runningCount);
        }

        runner.ReleaseAll();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_SerializesRunsForSameRepositoryRoot()
    {
        var queue = new CyberpilotRunQueue();
        var runner = new BlockingRunner(expectedConcurrentStarts: 1);
        using var provider = CreateProvider(queue, runner);
        await SeedRunsAsync(provider, "run-1", "owner/repo-one", "run-2", "owner/repo-one");
        var service = CreateService(provider, queue);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(CreateRequest("run-1", "owner/repo-one", "C:\\Repos\\One"));
        await queue.EnqueueAsync(CreateRequest("run-2", "owner/repo-one", "C:\\Repos\\One"));

        await runner.WaitForExpectedStartsAsync();
        await Task.Delay(100);

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>();
            var runningCount = await dbContext.PipelineRuns.CountAsync(run => run.Status == "Running");
            var queuedCount = await dbContext.PipelineRuns.CountAsync(run => run.Status == "Queued");
            Assert.Equal(1, runningCount);
            Assert.Equal(1, queuedCount);
        }

        runner.ReleaseAll();
        await service.StopAsync(CancellationToken.None);
    }

    private static CyberpilotPipelineService CreateService(ServiceProvider provider, ICyberpilotRunQueue queue)
    {
        var hubContext = provider.GetRequiredService<IHubContext<PipelineHub>>();
        return new CyberpilotPipelineService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            hubContext,
            Microsoft.Extensions.Options.Options.Create(new CyberpilotWebOptions()),
            NullLogger<CyberpilotPipelineService>.Instance);
    }

    private ServiceProvider CreateProvider(ICyberpilotRunQueue queue, BlockingRunner runner)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CyberpilotDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton(queue);
        services.AddSingleton<ICyberpilotRunner>(runner);
        services.AddSingleton<ILocalRepositoryValidator, PassthroughRepositoryValidator>();
        services.AddSingleton(CreateHubContext());
        services.AddSingleton<ILogger<SignalRProgressSink>>(NullLogger<SignalRProgressSink>.Instance);
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>().Database.EnsureCreated();
        return provider;
    }

    private static IHubContext<PipelineHub> CreateHubContext()
    {
        var hubContext = new Mock<IHubContext<PipelineHub>>();
        var clients = new Mock<IHubClients>();
        var groupClient = new Mock<IClientProxy>();
        hubContext.Setup(context => context.Clients).Returns(clients.Object);
        clients.Setup(item => item.Group(It.IsAny<string>())).Returns(groupClient.Object);
        groupClient
            .Setup(client => client.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return hubContext.Object;
    }

    private static WebPipelineRunRequest CreateRequest(string runId, string repository, string repoRoot)
        => new(runId, 1, repository, repoRoot, "C:\\Repos\\Cyberpilot", null, "gpt-4.1", false, TimeSpan.FromMinutes(10), false);

    private static async Task SeedRunsAsync(ServiceProvider provider, string firstRunId, string firstRepository, string secondRunId, string secondRepository)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>();
        dbContext.PipelineRuns.AddRange(
            new PipelineRun { Id = firstRunId, IssueNumber = 1, Repository = firstRepository, Model = "gpt-4.1", Status = "Queued", StageTimeoutMinutes = 10 },
            new PipelineRun { Id = secondRunId, IssueNumber = 2, Repository = secondRepository, Model = "gpt-4.1", Status = "Queued", StageTimeoutMinutes = 10 });
        await dbContext.SaveChangesAsync();
    }

    private sealed class BlockingRunner(int expectedConcurrentStarts) : ICyberpilotRunner
    {
        private readonly TaskCompletionSource expectedStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int started;

        public Task<CyberpilotRunResult> RunAsync(CyberpilotRunRequest request, CancellationToken cancellationToken = default)
            => RunAsync(request, new TextWriterProgressSink(TextWriter.Null, TextWriter.Null), cancellationToken);

        public async Task<CyberpilotRunResult> RunAsync(CyberpilotRunRequest request, ICyberpilotProgressSink progressSink, CancellationToken cancellationToken = default)
        {
            var currentStarted = Interlocked.Increment(ref started);
            if (currentStarted >= expectedConcurrentStarts)
            {
                expectedStarted.TrySetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
            return new CyberpilotRunResult(0, "deliver", "Completed", null, null, null, []);
        }

        public Task WaitForExpectedStartsAsync() => expectedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void ReleaseAll() => release.TrySetResult();
    }

    private sealed class PassthroughRepositoryValidator : ILocalRepositoryValidator
    {
        public Task<string> PrepareAsync(string repoRoot, string repository, string? githubToken, CancellationToken cancellationToken = default)
            => Task.FromResult(Path.GetFullPath(repoRoot));

        public Task<string> ValidateAsync(string repoRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(Path.GetFullPath(repoRoot));
    }
}
