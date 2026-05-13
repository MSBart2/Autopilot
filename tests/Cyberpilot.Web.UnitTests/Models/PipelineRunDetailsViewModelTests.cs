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
    public void ApprovalItems_FormatsAndOrdersPendingApprovalsFirst()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var decided = new PipelineApproval
        {
            RunId = run.Id,
            StageName = "review",
            Timing = "AfterStage",
            Reason = "Review approval was handled.",
            RequestedRole = "maintainer",
            ResumeStageName = "docs",
            Status = "Approved",
            CreatedAt = DateTime.Parse("2026-05-13T09:00:00Z").ToUniversalTime(),
            DecidedBy = "alice",
            DecisionReason = "ship it",
            DecidedAt = DateTime.Parse("2026-05-13T09:15:00Z").ToUniversalTime(),
        };
        var pending = new PipelineApproval
        {
            RunId = run.Id,
            StageName = "plan",
            Timing = "AfterStage",
            Reason = "Plan approval required before implementation.",
            RequestedRole = "maintainer",
            ResumeStageName = "implement",
            Status = "Pending",
            CreatedAt = DateTime.Parse("2026-05-13T10:00:00Z").ToUniversalTime(),
        };

        var vm = new PipelineRunDetailsViewModel(run, [], [], Approvals: [decided, pending]);

        Assert.True(vm.HasPendingApprovals);
        Assert.Collection(
            vm.ApprovalItems,
            approval =>
            {
                Assert.True(approval.IsPending);
                Assert.Equal("Plan · after stage", approval.StageTimingLabel);
                Assert.Equal("Plan approval required before implementation.", approval.Reason);
                Assert.Equal("maintainer", approval.RequestedRole);
                Assert.Equal("implement", approval.ResumeStageName);
            },
            approval =>
            {
                Assert.False(approval.IsPending);
                Assert.Equal("Review · after stage", approval.StageTimingLabel);
                Assert.Equal("alice", approval.DecidedBy);
                Assert.Equal("ship it", approval.DecisionReason);
            });
    }

    [Fact]
    public void ApprovalItems_WithoutApprovals_DefaultsToEmpty()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };

        var vm = new PipelineRunDetailsViewModel(run, []);

        Assert.Empty(vm.ApprovalItems);
        Assert.False(vm.HasPendingApprovals);
    }

    [Fact]
    public void EvidenceItems_FormatsAndOrdersEvidenceRows()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var evidence = new[]
        {
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "review",
                Kind = "required-action",
                Name = "required-action-1",
                Summary = "Fix failing tests.",
                CreatedAt = DateTime.Parse("2026-05-13T10:00:00Z").ToUniversalTime(),
            },
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "plan",
                Kind = "approval-request",
                Name = "approval-plan",
                Summary = "Approval requested for maintainer: Plan approval required.",
                CreatedAt = DateTime.Parse("2026-05-13T08:00:00Z").ToUniversalTime(),
            },
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "plan",
                Kind = "stage-artifact",
                Name = "plan-comment",
                Summary = "Implementation plan posted.",
                Uri = "https://github.com/owner/repo/issues/1#comment",
                MediaType = "text/markdown",
                CreatedAt = DateTime.Parse("2026-05-13T09:00:00Z").ToUniversalTime(),
            },
        };

        var vm = new PipelineRunDetailsViewModel(run, [], [], Evidence: evidence);

        Assert.Collection(
            vm.EvidenceItems,
            item =>
            {
                Assert.Equal("Plan", item.StageLabel);
                Assert.Equal("Approval", item.KindLabel);
                Assert.Equal("approval-plan", item.Name);
            },
            item =>
            {
                Assert.Equal("Plan", item.StageLabel);
                Assert.Equal("Artifact", item.KindLabel);
                Assert.Equal("plan-comment", item.Name);
                Assert.True(item.HasUri);
                Assert.Equal("text/markdown", item.MediaType);
            },
            item =>
            {
                Assert.Equal("Review", item.StageLabel);
                Assert.Equal("Action", item.KindLabel);
                Assert.Equal("Fix failing tests.", item.Summary);
                Assert.False(item.HasUri);
            });
    }

    [Fact]
    public void CanContinue_WithRejectedApproval_ReturnsFalse()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m", Status = "Stopped" };
        var approval = new PipelineApproval
        {
            RunId = run.Id,
            StageName = "review",
            Timing = "AfterStage",
            Reason = "Review approval required.",
            RequestedRole = "maintainer",
            ResumeStageName = "docs",
            Status = "Rejected",
        };

        var vm = new PipelineRunDetailsViewModel(run, [], [], Approvals: [approval]);

        Assert.True(vm.HasRejectedApprovals);
        Assert.False(vm.CanContinue);
        Assert.True(vm.ApprovalItems.Single().IsRejected);
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
