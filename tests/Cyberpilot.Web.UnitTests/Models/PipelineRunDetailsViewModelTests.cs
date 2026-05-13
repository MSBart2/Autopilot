using Cyberpilot.Persistence;
using Cyberpilot.Pipeline;
using Cyberpilot.Web.Models;

namespace Cyberpilot.Web.UnitTests.Models;

public class PipelineRunDetailsViewModelTests
{
    [Fact]
    public void Constructor_WithLabels_SetsAllProperties()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var logs = new[] { new PipelineStageLog { RunId = run.Id, StageName = "triage", Status = "GO" } };
        IReadOnlyList<string> labels = ["bug", "sdk"];

        var vm = new PipelineRunDetailsViewModel(run, logs, labels);

        Assert.Same(run, vm.Run);
        Assert.Single(vm.Logs);
        Assert.Equal(2, vm.Labels.Count);
    }

    [Fact]
    public void Constructor_WithoutLabels_DefaultsToEmpty()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var logs = Array.Empty<PipelineStageLog>();

        var vm = new PipelineRunDetailsViewModel(run, logs);

        Assert.Empty(vm.Labels);
    }

    [Fact]
    public void PipelineDefinitionLabels_DefaultToBuiltInMetadata()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };

        var vm = new PipelineRunDetailsViewModel(run, []);

        Assert.Equal($"{PipelineDefinitionDefaults.DefinitionName} v{PipelineDefinitionDefaults.DefinitionVersion}", vm.PipelineDefinitionLabel);
        Assert.Equal(PipelineDefinitionDefaults.PolicyProfileName, vm.PolicyProfileLabel);
        Assert.Equal(PipelineDefinitionDefaults.ContractVersion, vm.ContractVersionLabel);
    }

    [Fact]
    public void PipelineDefinitionLabels_UseRunMetadataWhenPresent()
    {
        var run = new PipelineRun
        {
            IssueNumber = 1,
            Repository = "r",
            Model = "m",
            PipelineDefinitionName = "review-only",
            PipelineDefinitionVersion = "2.0",
            PolicyProfileName = "strict",
            ContractVersion = "3.1",
        };

        var vm = new PipelineRunDetailsViewModel(run, []);

        Assert.Equal("review-only v2.0", vm.PipelineDefinitionLabel);
        Assert.Equal("strict", vm.PolicyProfileLabel);
        Assert.Equal("3.1", vm.ContractVersionLabel);
    }

    [Fact]
    public void StopDiagnostic_ForActiveRun_ReturnsNull()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Running" };
        var logs = Array.Empty<PipelineStageLog>();

        var vm = new PipelineRunDetailsViewModel(run, logs);

        Assert.Null(vm.StopDiagnostic);
    }

    [Fact]
    public void StopDiagnostic_ForCompletedRunWithBlockedHistory_ReturnsNull()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Completed" };
        var logs = new[]
        {
            new PipelineStageLog { RunId = run.Id, StageName = "review", Status = "STOP", CompletedAt = DateTime.UtcNow },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs);

        Assert.Null(vm.StopDiagnostic);
        Assert.False(vm.CanContinue);
        Assert.False(vm.CanReworkFromReview);
    }

    [Fact]
    public void StopDiagnostic_UsesHaltDispatchReasonAndReviewActions()
    {
        var run = new PipelineRun
        {
            IssueNumber = 1,
            Repository = "r",
            Model = "m",
            Status = "Stopped",
            CurrentStage = "review",
        };
        var logs = new[]
        {
            new PipelineStageLog { RunId = run.Id, StageName = "review", Status = "STOP", CompletedAt = DateTime.UtcNow },
        };
        var dispatches = new[]
        {
            new PipelineDispatch { RunId = run.Id, Type = "halt", Message = "Review did not approve the changes — halting for human intervention" },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs, [], Dispatches: dispatches);

        var diagnostic = Assert.IsType<PipelineStopDiagnostic>(vm.StopDiagnostic);
        Assert.Equal("warning", diagnostic.Severity);
        Assert.Equal("Review stopped", diagnostic.Title);
        Assert.Equal("Review did not approve the changes — halting for human intervention", diagnostic.Reason);
        Assert.Contains(diagnostic.CorrectiveActions, action => action.Contains("Open the linked PR", StringComparison.OrdinalIgnoreCase));
        Assert.True(vm.CanReworkFromReview);
    }

    [Fact]
    public void CanReworkFromReview_ForRemoteReviewRun_ReturnsFalse()
    {
        var run = new PipelineRun
        {
            IssueNumber = 1,
            Repository = "r",
            Model = "m",
            Status = "Failed",
            CurrentStage = "review",
            IsRemote = true,
        };

        var vm = new PipelineRunDetailsViewModel(run, []);

        Assert.False(vm.CanReworkFromReview);
    }

    [Fact]
    public void StopDiagnostic_PrefersStructuredCorrectiveActionsFromStageOutput()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Failed", CurrentStage = "plan" };
        var logs = new[]
        {
            new PipelineStageLog
            {
                RunId = run.Id,
                StageName = "plan",
                Status = "INVALID",
                Output = "```json\n{\"status\":\"STOP\",\"stop_reason\":\"Plan handoff comment is missing.\",\"corrective_actions\":[\"Rerun triage.\",\"Verify the triage handoff comment exists.\"]}\n```",
                CompletedAt = DateTime.UtcNow,
            },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs);

        var diagnostic = Assert.IsType<PipelineStopDiagnostic>(vm.StopDiagnostic);
        Assert.Equal("danger", diagnostic.Severity);
        Assert.Equal("Plan handoff comment is missing.", diagnostic.Reason);
        Assert.Equal(["Rerun triage.", "Verify the triage handoff comment exists."], diagnostic.CorrectiveActions);
        Assert.Equal("Plan result: STOP.", diagnostic.Evidence);
    }

    [Fact]
    public void GetStageRetryCount_CountsLogsForStage()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Failed" };
        var logs = new[]
        {
            new PipelineStageLog { RunId = run.Id, StageName = "plan", Status = "STOP" },
            new PipelineStageLog { RunId = run.Id, StageName = "plan", Status = "STOP" },
            new PipelineStageLog { RunId = run.Id, StageName = "triage", Status = "GO" },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs);

        Assert.Equal(2, vm.GetStageRetryCount("plan"));
        Assert.Equal(1, vm.GetStageRetryCount("triage"));
        Assert.Equal(0, vm.GetStageRetryCount("implement"));
    }

    [Fact]
    public void CanRetryStage_TerminalRunBelowCap_ReturnsTrue()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Failed", IsRemote = false };
        var logs = new[]
        {
            new PipelineStageLog { RunId = run.Id, StageName = "plan", Status = "STOP" },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs);

        Assert.True(vm.CanRetryStage("plan", 3));
    }

    [Fact]
    public void CanRetryStage_RemoteRun_ReturnsFalse()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Failed", IsRemote = true };

        var vm = new PipelineRunDetailsViewModel(run, []);

        Assert.False(vm.CanRetryStage("plan", 3));
    }

    [Fact]
    public void CanRetryStage_ActiveRun_ReturnsFalse()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Running", IsRemote = false };

        var vm = new PipelineRunDetailsViewModel(run, []);

        Assert.False(vm.CanRetryStage("plan", 3));
    }

    [Fact]
    public void CanRetryStage_UnknownStage_ReturnsFalse()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Failed", IsRemote = false };

        var vm = new PipelineRunDetailsViewModel(run, []);

        Assert.False(vm.CanRetryStage("unknown-stage", 3));
    }

    [Fact]
    public void CanRetryStage_AtCap_ReturnsFalse()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Failed", IsRemote = false };
        var logs = new[]
        {
            new PipelineStageLog { RunId = run.Id, StageName = "plan", Status = "STOP" },
            new PipelineStageLog { RunId = run.Id, StageName = "plan", Status = "STOP" },
            new PipelineStageLog { RunId = run.Id, StageName = "plan", Status = "STOP" },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs);

        Assert.False(vm.CanRetryStage("plan", 3));
    }
}
