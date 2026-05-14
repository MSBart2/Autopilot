# Approval Workflow

Cyberpilot treats human approval as a resumable pipeline state. An approval request is not a failed run; it is a controlled pause that records why the run stopped, who needs to decide, and where the run should resume after approval.

## When Approvals Are Created

The web runner can request an operator pause while an SDK run is active. When the SDK completes the current stage and sees the pause request, it creates a pending approval request with:

- the completed stage name
- the requested role, currently `operator`
- the reason for the pause
- the resume stage, normally the next stage after the completed one
- the request time

The run returns `Paused`, the approval is persisted in `PipelineApprovals`, and the Run Room shows the approval card.

## Approval States

| State | Meaning | Operator action |
|-------|---------|-----------------|
| `Pending` | The run is waiting for a human decision. | Approve or reject from the Run Room approval card. |
| `Approved` | The decision is recorded, but the run has not resumed yet. | Use Resume from the approval card. |
| `Rejected` | The operator blocked continuation. | Address the rejection with retry or rework before continuing. |

Delivered runs cannot be altered. Already decided approval requests cannot be changed.

## Approving And Resuming

Approving a request records the actor, optional reason, decision time, and approval evidence. It does not immediately start the run. Use the approval card's Resume action to enqueue the run at the approval's resume stage.

Resume is allowed only when:

- the run is terminal: `Failed`, `Stopped`, `Paused`, or `Cancelled`
- the approval is `Approved`
- the resume stage is recognized
- no other active run exists for the same repository issue
- the run has not already completed delivery

After resume, Cyberpilot clears the terminal state, queues the run, and continues from the recorded resume stage.

## Rejecting

Rejecting a request records the actor, optional reason, decision time, and rejection evidence. The run moves to `Stopped`, its current stage is set to the approval stage, and the run error explains the rejection.

Rejected approvals block normal Continue. Operators should use targeted retry or rework after addressing the rejection reason.

## Evidence

Approval decisions are added to the evidence ledger. This keeps the Run Room and landing reports focused on summarized facts rather than raw transcript text.

Evidence includes:

- approval identifier
- decision status
- stage and resume stage
- decision actor
- decision reason when supplied

## Compatibility

Older runs may not have approval rows or approval evidence. Treat missing approval data as historical absence, not as an approval or rejection.