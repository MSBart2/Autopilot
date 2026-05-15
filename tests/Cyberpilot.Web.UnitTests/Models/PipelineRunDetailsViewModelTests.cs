using System.Text.Json;
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
    public void PlanReview_WithStructuredPlanLog_FormatsPlanArtifact()
    {
        var run = new PipelineRun
        {
            IssueNumber = 1,
            Repository = "r",
            Model = "m",
            BranchName = "feat/issue-1-plan-review",
        };
        var stageResult = new StageResult(
            "GO",
            "unknown",
            true,
            null,
            ContractVersion: "1.0",
            Artifacts:
            [
                new StageArtifact("plan-comment", "Update the Run Room to render plan output as a review artifact.", MediaType: "text/markdown"),
                new StageArtifact("branch", "feat/issue-1-plan-review"),
            ],
            RequiredActions: ["Approve the plan before implementation."]);
        var logs = new[]
        {
            new PipelineStageLog
            {
                RunId = run.Id,
                StageName = "plan",
                Status = "GO",
                StageResultJson = JsonSerializer.Serialize(stageResult),
                StageResultContractVersion = "1.0",
                Output = "Full plan transcript",
                StartedAt = DateTime.Parse("2026-05-13T09:00:00Z").ToUniversalTime(),
                CompletedAt = DateTime.Parse("2026-05-13T09:05:00Z").ToUniversalTime(),
            },
        };
        var evidence = new[]
        {
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "plan",
                Kind = "policy-rationale",
                Name = "policy-rationale",
                Summary = "Plan requires maintainer approval.",
            },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs, [], Evidence: evidence);

        var planReview = Assert.IsType<PipelinePlanReviewViewModel>(vm.PlanReview);
        Assert.True(vm.HasPlanReview);
        Assert.Equal("GO", planReview.Status);
        Assert.Equal("feat/issue-1-plan-review", planReview.BranchName);
        Assert.Equal("Update the Run Room to render plan output as a review artifact.", planReview.Summary);
        Assert.Equal("1.0", planReview.ContractVersion);
        Assert.Equal(["Approve the plan before implementation."], planReview.RequiredActions);
        Assert.Collection(
            planReview.Artifacts,
            artifact =>
            {
                Assert.Equal("Plan", artifact.Label);
                Assert.True(artifact.IsPlanComment);
                Assert.Equal("text/markdown", artifact.MediaType);
            },
            artifact =>
            {
                Assert.Equal("Branch", artifact.Label);
                Assert.True(artifact.IsBranch);
            });
        Assert.Single(planReview.Evidence);
        Assert.Contains("maintainer approval", planReview.Evidence.Single().Summary);
    }

    [Fact]
    public void PlanReview_WithoutPlanLog_ReturnsNull()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var logs = new[]
        {
            new PipelineStageLog { RunId = run.Id, StageName = "triage", Status = "GO" },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs);

        Assert.Null(vm.PlanReview);
        Assert.False(vm.HasPlanReview);
    }

    [Fact]
    public void PlanReview_WithMalformedStageResult_FallsBackToOutput()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var logs = new[]
        {
            new PipelineStageLog
            {
                RunId = run.Id,
                StageName = "plan",
                Status = "STOP",
                StageResultJson = "{not valid json",
                Output = "Planner produced a human-readable fallback.",
                StartedAt = DateTime.Parse("2026-05-13T09:00:00Z").ToUniversalTime(),
            },
        };

        var vm = new PipelineRunDetailsViewModel(run, logs);

        var planReview = Assert.IsType<PipelinePlanReviewViewModel>(vm.PlanReview);
        Assert.Equal("STOP", planReview.Status);
        Assert.Equal("unknown", planReview.Decision);
        Assert.Equal("Planner produced a human-readable fallback.", planReview.Summary);
        Assert.Equal("Planner produced a human-readable fallback.", planReview.FullPlanText);
        Assert.Empty(planReview.Artifacts);
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
                StageName = "implement",
                Kind = "pull-request-reference",
                Name = "pull-request",
                Summary = "Pull request ready.",
                Uri = "https://github.com/owner/repo/pull/1",
                CreatedAt = DateTime.Parse("2026-05-13T09:30:00Z").ToUniversalTime(),
            },
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "deliver",
                Kind = "delivery-outcome",
                Name = "delivery-complete",
                Summary = "Delivery complete — PR merged, branch cleaned up, landing report posted",
                CreatedAt = DateTime.Parse("2026-05-13T11:00:00Z").ToUniversalTime(),
            },
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "review",
                Kind = "gate-outcome",
                Name = "gate:review-approved",
                Summary = "Gate 'review-approved' passed: Review approved the pull request.",
                CreatedAt = DateTime.Parse("2026-05-13T09:45:00Z").ToUniversalTime(),
            },
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
                StageName = "preflight",
                Kind = "repository-profile",
                Name = "target-repository",
                Summary = "Repository profile detected: languages: .NET.",
                CreatedAt = DateTime.Parse("2026-05-13T07:30:00Z").ToUniversalTime(),
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
                Kind = "usage-metrics",
                Name = "usage",
                Summary = "Usage: 10 input tokens, 5 output tokens, estimated cost $0.0001.",
                CreatedAt = DateTime.Parse("2026-05-13T09:15:00Z").ToUniversalTime(),
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
                Assert.Equal("Preflight", item.StageLabel);
                Assert.Equal("Repository", item.KindLabel);
                Assert.Equal("target-repository", item.Name);
            },
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
                Assert.Equal("Plan", item.StageLabel);
                Assert.Equal("Usage", item.KindLabel);
                Assert.Equal("usage", item.Name);
            },
            item =>
            {
                Assert.Equal("Implement", item.StageLabel);
                Assert.Equal("Pull Request", item.KindLabel);
                Assert.Equal("pull-request", item.Name);
                Assert.True(item.HasUri);
            },
            item =>
            {
                Assert.Equal("Review", item.StageLabel);
                Assert.Equal("Gate", item.KindLabel);
                Assert.Equal("gate:review-approved", item.Name);
            },
            item =>
            {
                Assert.Equal("Review", item.StageLabel);
                Assert.Equal("Action", item.KindLabel);
                Assert.Equal("Fix failing tests.", item.Summary);
                Assert.False(item.HasUri);
            },
            item =>
            {
                Assert.Equal("Deliver", item.StageLabel);
                Assert.Equal("Delivery", item.KindLabel);
                Assert.Equal("delivery-complete", item.Name);
            });
    }

    [Fact]
    public void PolicyItems_ReturnsOnlyPolicyRelevantEvidenceRows()
    {
        var run = new PipelineRun { IssueNumber = 1, Repository = "r", Model = "m" };
        var evidence = new[]
        {
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "plan",
                Kind = "stage-artifact",
                Name = "plan-comment",
                Summary = "Implementation plan posted.",
            },
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "review",
                Kind = "gate-outcome",
                Name = "gate:review-approved",
                Summary = "Gate 'review-approved' passed: Review approved the pull request.",
            },
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "review",
                Kind = "policy-rationale",
                Name = "policy-rationale",
                Summary = "Policy requires passing tests before delivery.",
            },
            new PipelineEvidence
            {
                RunId = run.Id,
                StageName = "review",
                Kind = "required-action",
                Name = "required-action-1",
                Summary = "Fix failing tests.",
            },
        };

        var vm = new PipelineRunDetailsViewModel(run, [], [], Evidence: evidence);

        Assert.True(vm.HasPolicyItems);
        Assert.Collection(
            vm.PolicyItems,
            item => Assert.Equal("gate:review-approved", item.Name),
            item => Assert.Equal("policy-rationale", item.Name),
            item => Assert.Equal("required-action-1", item.Name));
        Assert.Equal(4, vm.EvidenceItems.Count);
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
