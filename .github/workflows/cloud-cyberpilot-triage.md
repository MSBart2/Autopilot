---
name: "Cyberpilot — Triage"
description: "Classifies issues and kicks off the planning stage"

on:
  workflow_dispatch:
    inputs:
      issue_number:
        description: "Issue number to triage"
        required: true
        type: string

runs-on: ${{ vars.PIPELINE_RUNNER }}
engine: copilot

imports:
  - .github/agents/triage.agent.md

permissions:
  contents: read
  issues: read
  pull-requests: read

tools:
  github:
    toolsets: [default]

safe-outputs:
  add-comment:
    max: 1
    target: "triggering"
  add-labels:
    allowed: [bug, enhancement, feature, security, documentation, refactor, "cloud/triage", "cloud/planning"]
    max: 3
    target: "triggering"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  remove-labels:
    allowed: ["cloud/triage", "cloud/planning", "cloud/implementing", "cloud/review", "cloud/awaiting-merge", "cloud/done"]
    max: 7
    target: "triggering"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  dispatch-workflow: [cloud-plan]
---

## Pipeline — Triage Agent

Run the imported `triage` agent instructions as the classification policy for the cloud AI-SDLC pipeline.

**Target issue:** #${{ github.event.inputs.issue_number }}

## Cloud Duties

1. Remove existing `cloud/*` labels and add `cloud/triage` before classification.
2. Read issue #${{ github.event.inputs.issue_number }} and classify it using the imported triage policy.
3. Post one cloud triage handoff comment headed `## 🏷️ Pipeline — Triage`.
4. Apply 1-2 classification labels from the allowed type labels.
5. Replace `cloud/triage` with `cloud/planning`.
6. Dispatch `cloud-plan` with `issue_number` set to `${{ github.event.inputs.issue_number }}`.

## Cloud Overrides

- Cloud labels and dispatching rules in this workflow override any local label guidance in the imported agent.
- Every triggered issue is classified; do not reject or skip unless GitHub access fails.
- If dispatch fails, call `noop` with the reason.
- The triage comment must contain enough context for `cloud-plan` to identify classification, scope, and agents needed.
