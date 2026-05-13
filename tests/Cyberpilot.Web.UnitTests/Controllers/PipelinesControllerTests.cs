using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Web.Controllers;
using Cyberpilot.Web.Models;
using Cyberpilot.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cyberpilot.Web.UnitTests.Controllers;

public class PipelinesControllerTests
{
    [Fact]
    public async Task Index_ReturnsDashboardWithRuns()
    {
        var controller = CreateController();

        var result = Assert.IsType<ViewResult>(await controller.Index());

        var model = Assert.IsType<PipelineDashboardViewModel>(result.Model);
        Assert.Empty(model.Runs);
    }

    [Fact]
    public void Guide_WithUnknownMode_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = controller.Guide("unknown");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Guide_WithKnownMode_ReturnsGuideViewModel()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(repositoryRoot, "web");
        var (controller, _, _, _) = CreateControllerWithDependencies(contentRootPath: webRoot);

        var result = Assert.IsType<ViewResult>(controller.Guide("sdk"));

        var model = Assert.IsType<PipelineGuideViewModel>(result.Model);
        Assert.Equal("SDK", model.Mode);
        Assert.Contains("Programmatic Copilot SDK execution", model.Summary);
        Assert.Contains("<h1", model.HtmlContent, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AI-SDLC.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for guide tests.");
    }

    private static PipelinesController CreateController()
    {
        var (controller, _) = CreateControllerWithContext();
        return controller;
    }

    private static (PipelinesController Controller, CyberpilotDbContext DbContext) CreateControllerWithContext()
    {
        var (controller, dbContext, _, _) = CreateControllerWithDependencies();
        return (controller, dbContext);
    }

    private static (PipelinesController Controller, CyberpilotDbContext DbContext, TestRunQueue Queue, TestRepositoryConnectionStore ConnectionStore) CreateControllerWithDependencies(CyberpilotWebOptions? webOptions = null, string? contentRootPath = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CyberpilotDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new CyberpilotDbContext(options);
        dbContext.Database.EnsureCreated();
        var queue = new TestRunQueue();
        var connectionStore = new TestRepositoryConnectionStore();
        var environment = new TestEnvironment();
        if (!string.IsNullOrWhiteSpace(contentRootPath))
        {
            environment.ContentRootPath = contentRootPath;
        }

        var controller = new PipelinesController(
            dbContext,
            environment,
            queue,
            new TestIssueClient(),
            new TestIssueClientFactory(),
            connectionStore,
            new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(webOptions ?? new CyberpilotWebOptions { Repository = "rbmathis/Cyberpilot" }),
            NullLogger<PipelinesController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());

        return (controller, dbContext, queue, connectionStore);
    }

    [Fact]
    public async Task Issues_ReturnsViewWithViewModel()
    {
        var controller = CreateController();

        var result = Assert.IsType<ViewResult>(await controller.Issues());

        Assert.IsType<PipelineIssuesViewModel>(result.Model);
    }

    [Fact]
    public async Task Details_ExistingRun_ReturnsViewWithDetails()
    {
        var (controller, db) = CreateControllerWithContext();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "claude-sonnet-4.6" };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "plan",
            Timing = "AfterStage",
            Reason = "Plan approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "implement",
        });
        await db.SaveChangesAsync();

        var result = Assert.IsType<ViewResult>(await controller.Details(run.Id));

        var model = Assert.IsType<PipelineRunDetailsViewModel>(result.Model);
        Assert.Single(model.ApprovalItems);
        Assert.True(model.HasPendingApprovals);
    }

    [Fact]
    public async Task Details_ConfiguredRepository_LoadsIssueDetailsFromTargetRepository()
    {
        var options = new CyberpilotWebOptions
        {
            Repository = "owner/repo",
            Repositories =
            [
                new ConfiguredRepositoryOptions { Name = "Repo", Repository = "owner/repo", RepoRoot = "C:\\Repos\\Repo", Token = "configured-token" }
            ]
        };
        var (controller, db, _, _) = CreateControllerWithDependencies(options);
        var run = new PipelineRun { IssueNumber = 7, Repository = "owner/repo", Model = "claude-sonnet-4.6" };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<ViewResult>(await controller.Details(run.Id));

        var model = Assert.IsType<PipelineRunDetailsViewModel>(result.Model);
        Assert.NotNull(model.Issue);
        Assert.Equal("Issue", model.Issue.Title);
        Assert.Equal("Issue details", model.Issue.Body);
    }

    [Fact]
    public async Task Details_NonExistentRun_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.Details("nonexistent-id");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Start_ValidRequest_CreatesRunAndRedirects()
    {
        var (controller, db) = CreateControllerWithContext();
        var request = new PipelineStartRequest { IssueNumber = 1, Repository = "rbmathis/Cyberpilot", Model = "claude-sonnet-4.6" };

        var result = Assert.IsType<RedirectToActionResult>(await controller.Start(request));

        Assert.Equal("Details", result.ActionName);
        Assert.Single(db.PipelineRuns);
    }

    [Fact]
    public async Task LoadIssues_ValidConnection_ReturnsIssuesAndConnectionId()
    {
        var controller = CreateController();
        var request = new PipelineIssueLoadRequest { RepositoryUrl = "https://github.com/owner/repo", Token = "token" };

        var result = Assert.IsType<ViewResult>(await controller.LoadIssues(request));

        Assert.Equal("Issues", result.ViewName);
        var model = Assert.IsType<PipelineIssuesViewModel>(result.Model);
        Assert.Equal("owner/repo", model.Repository);
        Assert.False(string.IsNullOrWhiteSpace(model.ConnectionId));
        Assert.Single(model.Issues);
    }

    [Fact]
    public async Task LoadConfiguredIssues_ValidConfiguredRepository_ReturnsIssuesAndConnectionId()
    {
        var options = new CyberpilotWebOptions
        {
            Repository = "owner/repo",
            Repositories =
            [
                new ConfiguredRepositoryOptions { Name = "Configured", Repository = "https://github.com/owner/repo", RepoRoot = "C:\\Repos\\Repo", Token = "configured-token" }
            ]
        };
        var (controller, _, _, _) = CreateControllerWithDependencies(options);
        var request = new PipelineConfiguredIssueLoadRequest { Repository = "owner/repo" };

        var result = Assert.IsType<ViewResult>(await controller.LoadConfiguredIssues(request));

        Assert.Equal("Issues", result.ViewName);
        var model = Assert.IsType<PipelineIssuesViewModel>(result.Model);
        Assert.Equal("owner/repo", model.Repository);
        Assert.False(string.IsNullOrWhiteSpace(model.ConnectionId));
        Assert.Single(model.ConfiguredRepositories);
        Assert.Equal(Path.GetFullPath("C:\\Repos\\Repo"), model.ConfiguredRepositories[0].RepoRoot);
        Assert.Single(model.Issues);
    }

    [Fact]
    public async Task Start_WithConnection_QueuesRunWithConnectionToken()
    {
        var (controller, db, queue, store) = CreateControllerWithDependencies();
        var connectionId = store.Save("owner/repo", "C:\\Repos\\Repo", "token-value");
        var request = new PipelineStartRequest
        {
            IssueNumber = 7,
            Repository = "https://github.com/owner/repo",
            ConnectionId = connectionId,
            Model = "claude-sonnet-4.6"
        };

        var result = Assert.IsType<RedirectToActionResult>(await controller.Start(request));

        Assert.Equal("Details", result.ActionName);
        var run = Assert.Single(db.PipelineRuns);
        Assert.Equal("owner/repo", run.Repository);
        Assert.NotNull(queue.LastRequest);
        Assert.Equal("owner/repo", queue.LastRequest.Repository);
        Assert.Equal("C:\\Repos\\Repo", queue.LastRequest.RepoRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), queue.LastRequest.AgentPromptRoot);
        Assert.Equal("token-value", queue.LastRequest.GitHubToken);
    }

    [Fact]
    public async Task Start_WithConfiguredAgentPromptRoot_QueuesRunWithPromptRoot()
    {
        var options = new CyberpilotWebOptions { Repository = "owner/repo", AgentPromptRoot = "C:\\Repos\\Cyberpilot" };
        var (controller, _, queue, store) = CreateControllerWithDependencies(options);
        var connectionId = store.Save("owner/repo", "C:\\Repos\\Repo", "token-value");
        var request = new PipelineStartRequest
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            ConnectionId = connectionId,
            Model = "claude-sonnet-4.6"
        };

        await controller.Start(request);

        Assert.NotNull(queue.LastRequest);
        Assert.Equal(Path.GetFullPath("C:\\Repos\\Cyberpilot"), queue.LastRequest.AgentPromptRoot);
    }

    [Fact]
    public async Task Start_InvalidModelState_RedirectsToIssues()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError("IssueNumber", "Required");
        var request = new PipelineStartRequest();

        var result = Assert.IsType<RedirectToActionResult>(await controller.Start(request));

        Assert.Equal("Issues", result.ActionName);
    }

    [Fact]
    public async Task Start_ActiveRunExists_RedirectsToIssuesWithError()
    {
        var (controller, db) = CreateControllerWithContext();
        db.PipelineRuns.Add(new PipelineRun { IssueNumber = 42, Repository = "owner/repo", Model = "gpt-4.1", Status = "Running" });
        await db.SaveChangesAsync();

        var request = new PipelineStartRequest { IssueNumber = 42, Repository = "owner/repo", Model = "claude-sonnet-4.6" };
        var result = Assert.IsType<RedirectToActionResult>(await controller.Start(request));

        Assert.Equal("Issues", result.ActionName);
    }

    [Fact]
    public async Task Cancel_ExistingQueuedRun_MarksAsCancelled()
    {
        var (controller, db) = CreateControllerWithContext();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "gpt-4.1", Status = "Queued" };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        await controller.Cancel(run.Id);

        var updated = await db.PipelineRuns.FirstAsync(r => r.Id == run.Id);
        Assert.Equal("Cancelled", updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task Cancel_CompletedRun_DoesNotChangeStatus()
    {
        var (controller, db) = CreateControllerWithContext();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "gpt-4.1", Status = "Completed" };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        await controller.Cancel(run.Id);

        var updated = await db.PipelineRuns.FirstAsync(r => r.Id == run.Id);
        Assert.Equal("Completed", updated.Status);
    }

    [Fact]
    public async Task Cancel_NonExistentRun_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.Cancel("nonexistent");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Continue_TerminalRun_RequeuesSameRun()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Stopped",
            CompletedAt = DateTime.UtcNow,
            Error = "Needs human input.",
            SkipDeliver = true,
            StageTimeoutMinutes = 10,
            AllowMissingDocs = true,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.Continue(run.Id));

        Assert.Equal("Details", result.ActionName);
        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("Queued", updated.Status);
        Assert.Null(updated.CompletedAt);
        Assert.Null(updated.Error);
        Assert.NotNull(queue.LastRequest);
        Assert.Equal(run.Id, queue.LastRequest.RunId);
        Assert.Equal("owner/repo", queue.LastRequest.Repository);
        Assert.True(queue.LastRequest.SkipDeliver);
        Assert.True(queue.LastRequest.AllowMissingDocs);
    }

    [Fact]
    public async Task Continue_ActiveRun_DoesNotRequeue()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun { IssueNumber = 7, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Running" };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.Continue(run.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.Equal("Running", (await db.PipelineRuns.FirstAsync(item => item.Id == run.Id)).Status);
    }

    [Fact]
    public async Task Continue_CompletedRun_DoesNotRequeue()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Completed",
            CurrentStage = "review",
            CompletedAt = DateTime.UtcNow,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.Continue(run.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.Equal("Completed", (await db.PipelineRuns.FirstAsync(item => item.Id == run.Id)).Status);
        Assert.Equal("This run cannot be continued from its current status.", controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task Continue_PendingApproval_DoesNotRequeue()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Paused",
            CurrentStage = "implement",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(new PipelineApproval
        {
            Id = "approval-continue-block",
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "plan",
            Timing = "AfterStage",
            Reason = "Plan approval required.",
            RequestedRole = "operator",
            ResumeStageName = "implement",
        });
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.Continue(run.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.Equal("Paused", (await db.PipelineRuns.FirstAsync(item => item.Id == run.Id)).Status);
        Assert.Equal("Resolve pending approvals before continuing this run.", controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task Continue_ConfiguredRepository_UsesConfiguredRootAndToken()
    {
        var options = new CyberpilotWebOptions
        {
            Repository = "owner/repo",
            Repositories =
            [
                new ConfiguredRepositoryOptions { Name = "Repo", Repository = "owner/repo", RepoRoot = "C:\\Repos\\Repo", Token = "configured-token" }
            ]
        };
        var (controller, db, queue, _) = CreateControllerWithDependencies(options);
        var run = new PipelineRun { IssueNumber = 7, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Failed", StageTimeoutMinutes = 10 };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        await controller.Continue(run.Id);

        Assert.NotNull(queue.LastRequest);
        Assert.Equal(Path.GetFullPath("C:\\Repos\\Repo"), queue.LastRequest.RepoRoot);
        Assert.Equal("configured-token", queue.LastRequest.GitHubToken);
    }

    [Fact]
    public async Task ReworkFromReview_StoppedReviewRun_RequeuesFromImplement()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Failed",
            CurrentStage = "review",
            BranchName = "cyberpilot/issue-7",
            PrUrl = "https://github.com/owner/repo/pull/10",
            CompletedAt = DateTime.UtcNow,
            Error = "Review did not approve the changes.",
            SkipDeliver = true,
            StageTimeoutMinutes = 10,
            AllowMissingDocs = true,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ReworkFromReview(run.Id));

        Assert.Equal("Details", result.ActionName);
        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("Queued", updated.Status);
        Assert.Equal("implement", updated.CurrentStage);
        Assert.Equal("cyberpilot/issue-7", updated.BranchName);
        Assert.Equal("https://github.com/owner/repo/pull/10", updated.PrUrl);
        Assert.Null(updated.CompletedAt);
        Assert.Null(updated.Error);
        Assert.NotNull(queue.LastRequest);
        Assert.Equal(run.Id, queue.LastRequest.RunId);
        Assert.Equal("implement", queue.LastRequest.StartStage);
        Assert.Equal("Review feedback routed back to implementation.", queue.LastRequest.RetryReason);
        Assert.True(queue.LastRequest.SkipDeliver);
        Assert.True(queue.LastRequest.AllowMissingDocs);
        Assert.Equal("Review feedback routed back to implementation. Cyberpilot will update the existing PR branch, then return to review.", controller.TempData["PipelineNotice"]);
    }

    [Fact]
    public async Task ReworkFromReview_LatestBlockedReviewLog_RequeuesFromImplement()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Stopped",
            CurrentStage = "pipeline",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        db.PipelineStageLogs.Add(new PipelineStageLog
        {
            RunId = run.Id,
            StageName = "review",
            Status = "STOP",
            CompletedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await controller.ReworkFromReview(run.Id);

        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("implement", updated.CurrentStage);
        Assert.NotNull(queue.LastRequest);
        Assert.Equal("implement", queue.LastRequest.StartStage);
    }

    [Fact]
    public async Task ReworkFromReview_NonReviewRun_DoesNotRequeue()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Failed",
            CurrentStage = "plan",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ReworkFromReview(run.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.Equal("plan", (await db.PipelineRuns.FirstAsync(item => item.Id == run.Id)).CurrentStage);
        Assert.Equal("Rework from Review is only available for stopped or failed review runs.", controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task ResetMission_TerminalRun_RemovesLocalRunAndLogs()
    {
        var (controller, db, _, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Stopped",
            CurrentStage = "triage",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        db.PipelineStageLogs.Add(new PipelineStageLog { RunId = run.Id, StageName = "triage", Status = "Stopped", Output = "output" });
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ResetMission(run.Id));

        Assert.Equal("Index", result.ActionName);
        Assert.Empty(await db.PipelineRuns.Where(item => item.Id == run.Id).ToArrayAsync());
        Assert.Empty(await db.PipelineStageLogs.Where(item => item.RunId == run.Id).ToArrayAsync());
    }

    [Fact]
    public async Task ResetMission_DeliveredRun_DoesNotRemoveLocalRun()
    {
        var (controller, db, _, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Completed",
            CurrentStage = "deliver",
            CompletedAt = DateTime.UtcNow,
            SkipDeliver = false,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ResetMission(run.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.True(await db.PipelineRuns.AnyAsync(item => item.Id == run.Id));
        Assert.Equal("Reset Mission is not available after the code has been delivered.", controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task RetryStage_ValidStageName_RequeuesRunFromThatStage()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Failed",
            CurrentStage = "review",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "implement", RetryReason = "Need to apply review feedback." }));

        Assert.Equal("Details", result.ActionName);
        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("Queued", updated.Status);
        Assert.Equal("implement", updated.CurrentStage);
        Assert.Null(updated.CompletedAt);
        Assert.Null(updated.Error);
        Assert.NotNull(queue.LastRequest);
        Assert.Equal("implement", queue.LastRequest.StartStage);
        Assert.Equal("Need to apply review feedback.", queue.LastRequest.RetryReason);
    }

    [Fact]
    public async Task RetryStage_WithModelOverride_UpdatesRunModel()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Failed",
            CurrentStage = "plan",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "plan", Model = "gpt-4.1" });

        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("gpt-4.1", updated.Model);
    }

    [Fact]
    public async Task RetryStage_WithTimeoutOverride_UpdatesRunTimeout()
    {
        var (controller, db, _, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Stopped",
            CurrentStage = "implement",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "implement", StageTimeoutMinutes = 45 });

        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal(45, updated.StageTimeoutMinutes);
    }

    [Fact]
    public async Task RetryStage_UnknownStageName_RedirectsWithError()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Failed",
            CurrentStage = "plan",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "unknown-stage" }));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.NotNull(controller.TempData["PipelineError"]);
        Assert.Contains("unknown-stage", controller.TempData["PipelineError"]!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryStage_ActiveRun_RedirectsWithError()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Running",
            CurrentStage = "implement",
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "implement" }));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.Equal("This run is already active.", controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task RetryStage_ExceedsMaxRetries_RedirectsWithError()
    {
        var options = new CyberpilotWebOptions { Repository = "owner/repo", MaxStageRetries = 2 };
        var (controller, db, queue, _) = CreateControllerWithDependencies(options);
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Failed",
            CurrentStage = "triage",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        // Insert 2 stage logs for the same stage — MaxStageRetries is 2, so this exceeds cap
        db.PipelineStageLogs.Add(new PipelineStageLog { RunId = run.Id, StageName = "triage", Status = "STOP", RetryCount = 0 });
        db.PipelineStageLogs.Add(new PipelineStageLog { RunId = run.Id, StageName = "triage", Status = "STOP", RetryCount = 1 });
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "triage" }));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.NotNull(controller.TempData["PipelineError"]);
        Assert.Contains("Maximum retry", controller.TempData["PipelineError"]!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryStage_NonExistentRun_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.RetryStage("nonexistent-id", new RetryStageRequest { StageName = "plan" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RetryStage_AnotherActiveRunExists_RedirectsWithError()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Failed",
            CurrentStage = "triage",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        var activeRun = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Running",
        };
        db.PipelineRuns.Add(run);
        db.PipelineRuns.Add(activeRun);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "triage" }));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.NotNull(controller.TempData["PipelineError"]);
        Assert.Contains("already has an active", controller.TempData["PipelineError"]!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryStage_PausedRun_RequeuesRunFromThatStage()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 9,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Paused",
            CurrentStage = "review",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "implement" }));

        Assert.Equal("Details", result.ActionName);
        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("Queued", updated.Status);
        Assert.Equal("implement", updated.CurrentStage);
        Assert.Null(updated.CompletedAt);
        Assert.NotNull(queue.LastRequest);
    }

    [Fact]
    public async Task RetryStage_CancelledRun_RequeuesRunFromThatStage()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 11,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Cancelled",
            CurrentStage = "docs",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.RetryStage(run.Id, new RetryStageRequest { StageName = "docs" }));

        Assert.Equal("Details", result.ActionName);
        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("Queued", updated.Status);
        Assert.Equal("docs", updated.CurrentStage);
        Assert.Null(updated.CompletedAt);
        Assert.NotNull(queue.LastRequest);
    }

    [Fact]
    public async Task ApproveApproval_PendingApproval_RecordsDecision()
    {
        var (controller, db) = CreateControllerWithContext();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Paused" };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "plan",
            Timing = "AfterStage",
            Reason = "Plan approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "implement",
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ApproveApproval(run.Id, approval.Id, new PipelineApprovalDecisionRequest { Reason = "  ship it  " }));

        Assert.Equal("Details", result.ActionName);
        var updated = await db.PipelineApprovals.FirstAsync(item => item.Id == approval.Id);
        Assert.Equal("Approved", updated.Status);
        Assert.Equal("operator", updated.DecidedBy);
        Assert.Equal("ship it", updated.DecisionReason);
        Assert.NotNull(updated.DecidedAt);
        Assert.NotNull(controller.TempData["PipelineNotice"]);
    }

    [Fact]
    public async Task RejectApproval_PendingApproval_RecordsDecision()
    {
        var (controller, db) = CreateControllerWithContext();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Paused" };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "review",
            Timing = "AfterStage",
            Reason = "Review approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "docs",
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.RejectApproval(run.Id, approval.Id, new PipelineApprovalDecisionRequest { Reason = "needs work" }));

        Assert.Equal("Details", result.ActionName);
        var updated = await db.PipelineApprovals.FirstAsync(item => item.Id == approval.Id);
        Assert.Equal("Rejected", updated.Status);
        Assert.Equal("needs work", updated.DecisionReason);
        Assert.NotNull(updated.DecidedAt);

        var updatedRun = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("Stopped", updatedRun.Status);
        Assert.Equal("review", updatedRun.CurrentStage);
        Assert.Contains("needs work", updatedRun.Error);
    }

    [Fact]
    public async Task Continue_RejectedApproval_DoesNotRequeue()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 7,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Stopped",
            CurrentStage = "review",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(new PipelineApproval
        {
            Id = "approval-continue-rejected",
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "review",
            Timing = "AfterStage",
            Reason = "Review approval required.",
            RequestedRole = "operator",
            ResumeStageName = "docs",
            Status = "Rejected",
            DecidedBy = "operator",
            DecisionReason = "needs changes",
            DecidedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.Continue(run.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.Equal("Stopped", (await db.PipelineRuns.FirstAsync(item => item.Id == run.Id)).Status);
        Assert.Equal("Rejected approvals must be addressed with a targeted retry or rework before this run can continue.", controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task ApproveApproval_DeliveredRun_DoesNotAlterApproval()
    {
        var (controller, db) = CreateControllerWithContext();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Completed", SkipDeliver = false };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "deliver",
            Timing = "BeforeStage",
            Reason = "Delivery approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "deliver",
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ApproveApproval(run.Id, approval.Id, new PipelineApprovalDecisionRequest()));

        Assert.Equal("Details", result.ActionName);
        var unchanged = await db.PipelineApprovals.FirstAsync(item => item.Id == approval.Id);
        Assert.Equal("Pending", unchanged.Status);
        Assert.Null(unchanged.DecidedAt);
        Assert.NotNull(controller.TempData["PipelineError"]);
        Assert.Contains("Delivered", controller.TempData["PipelineError"]!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveApproval_DecidedApproval_DoesNotAlterDecision()
    {
        var (controller, db) = CreateControllerWithContext();
        var decidedAt = DateTime.UtcNow.AddMinutes(-5);
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Paused" };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "plan",
            Timing = "AfterStage",
            Reason = "Plan approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "implement",
            Status = "Rejected",
            DecidedBy = "alice",
            DecisionReason = "nope",
            DecidedAt = decidedAt,
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ApproveApproval(run.Id, approval.Id, new PipelineApprovalDecisionRequest { Reason = "changed" }));

        Assert.Equal("Details", result.ActionName);
        var unchanged = await db.PipelineApprovals.FirstAsync(item => item.Id == approval.Id);
        Assert.Equal("Rejected", unchanged.Status);
        Assert.Equal("alice", unchanged.DecidedBy);
        Assert.Equal("nope", unchanged.DecisionReason);
        Assert.Equal(decidedAt, unchanged.DecidedAt);
        Assert.NotNull(controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task ApproveApproval_MissingApproval_ReturnsNotFound()
    {
        var (controller, db) = CreateControllerWithContext();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Paused" };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync();

        var result = await controller.ApproveApproval(run.Id, "missing", new PipelineApprovalDecisionRequest());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ResumeApproval_ApprovedApproval_RequeuesRunFromResumeStage()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun
        {
            IssueNumber = 1,
            Repository = "owner/repo",
            Model = "claude-sonnet-4.6",
            Status = "Paused",
            CurrentStage = "plan",
            CompletedAt = DateTime.UtcNow,
            StageTimeoutMinutes = 10,
        };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "plan",
            Timing = "AfterStage",
            Reason = "Plan approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "implement",
            Status = "Approved",
            DecidedBy = "alice",
            DecidedAt = DateTime.UtcNow,
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ResumeApproval(run.Id, approval.Id));

        Assert.Equal("Details", result.ActionName);
        var updated = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("Queued", updated.Status);
        Assert.Equal("implement", updated.CurrentStage);
        Assert.Null(updated.CompletedAt);
        Assert.NotNull(queue.LastRequest);
        Assert.Equal("implement", queue.LastRequest.StartStage);
        Assert.Contains("Approval", queue.LastRequest.RetryReason);
    }

    [Fact]
    public async Task ResumeApproval_PendingApproval_DoesNotRequeueRun()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Paused", StageTimeoutMinutes = 10 };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "plan",
            Timing = "AfterStage",
            Reason = "Plan approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "implement",
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ResumeApproval(run.Id, approval.Id));

        Assert.Equal("Details", result.ActionName);
        var unchanged = await db.PipelineRuns.FirstAsync(item => item.Id == run.Id);
        Assert.Equal("Paused", unchanged.Status);
        Assert.Null(queue.LastRequest);
        Assert.NotNull(controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task ResumeApproval_AnotherActiveRunExists_DoesNotRequeueRun()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun { IssueNumber = 7, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Paused", StageTimeoutMinutes = 10 };
        var activeRun = new PipelineRun { IssueNumber = 7, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Running" };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "review",
            Timing = "AfterStage",
            Reason = "Review approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "docs",
            Status = "Approved",
            DecidedBy = "alice",
            DecidedAt = DateTime.UtcNow,
        };
        db.PipelineRuns.Add(run);
        db.PipelineRuns.Add(activeRun);
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ResumeApproval(run.Id, approval.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.NotNull(controller.TempData["PipelineError"]);
    }

    [Fact]
    public async Task ResumeApproval_DeliveredRun_DoesNotRequeueRun()
    {
        var (controller, db, queue, _) = CreateControllerWithDependencies();
        var run = new PipelineRun { IssueNumber = 1, Repository = "owner/repo", Model = "claude-sonnet-4.6", Status = "Completed", SkipDeliver = false, StageTimeoutMinutes = 10 };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            IssueNumber = run.IssueNumber,
            StageName = "deliver",
            Timing = "BeforeStage",
            Reason = "Delivery approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "deliver",
            Status = "Approved",
            DecidedBy = "alice",
            DecidedAt = DateTime.UtcNow,
        };
        db.PipelineRuns.Add(run);
        db.PipelineApprovals.Add(approval);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(await controller.ResumeApproval(run.Id, approval.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Null(queue.LastRequest);
        Assert.NotNull(controller.TempData["PipelineError"]);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private sealed class TestRunQueue : ICyberpilotRunQueue
    {
        public WebPipelineRunRequest? LastRequest { get; private set; }

        public ValueTask EnqueueAsync(WebPipelineRunRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return ValueTask.CompletedTask;
        }

        public ValueTask<WebPipelineRunRequest> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestIssueClient : IGitHubIssueClient
    {
        private readonly bool includeIssues;
        public List<string> RemovedLabels { get; } = [];
        public List<string> AddedLabels { get; } = [];
        public List<long> DeletedComments { get; } = [];

        public TestIssueClient(bool includeIssues = false)
        {
            this.includeIssues = includeIssues;
        }

        public Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default)
        {
            AddedLabels.Add(label);
            return Task.CompletedTask;
        }

        public Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<GitHubIssueComment> comments =
            [
                new(1, "## 🕵️ Case File — Triage Report\nFound something.", "github-copilot"),
                new(2, "Human note", "octocat"),
            ];
            return Task.FromResult(comments);
        }

        public Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default)
        {
            DeletedComments.Add(commentId);
            return Task.CompletedTask;
        }

        public Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            GitHubIssueSummary? issue = includeIssues
                ? new(issueNumber, "Issue", $"https://github.com/owner/repo/issues/{issueNumber}", ["bug"], DateTimeOffset.UtcNow, "OPEN", false, "Issue details")
                : null;
            return Task.FromResult(issue);
        }

        public Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["sdk", "sdk/triage", "bug"]);

        public Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult("OPEN");

        public Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<GitHubIssueSummary> issues = includeIssues
                ? [new(1, "Issue", "https://github.com/owner/repo/issues/1", [], DateTimeOffset.UtcNow, "OPEN", false, "Issue details")]
                : [];
            return Task.FromResult(issues);
        }

        public Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default)
        {
            RemovedLabels.Add(label);
            return Task.CompletedTask;
        }

        public Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubPullRequestInfo?>(null);
    }

    private sealed class TestIssueClientFactory : IGitHubIssueClientFactory
    {
        public IGitHubIssueClient Create(string repository, string token) => new TestIssueClient(includeIssues: true);
    }

    private sealed class TestRepositoryConnectionStore : IRepositoryConnectionStore
    {
        private readonly Dictionary<string, RepositoryConnection> connections = new(StringComparer.OrdinalIgnoreCase);

        public string Save(string repository, string repoRoot, string token)
        {
            var id = "connection-1";
            connections[id] = new RepositoryConnection(id, repository, repoRoot, token);
            return id;
        }

        public RepositoryConnection? Get(string? id)
            => id is not null && connections.TryGetValue(id, out var connection) ? connection : null;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Cyberpilot.Web";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public string EnvironmentName { get; set; } = "Testing";

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}