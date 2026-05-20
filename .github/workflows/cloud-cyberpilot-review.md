---
name: "Cyberpilot — Review"
description: "Multi-agent code review on pull requests"

on:
  label_command:
    name: cloud/review
    events: [issues]
  workflow_dispatch:
    inputs:
      issue_number:
        description: "Issue number to review"
        required: false
        type: string

runs-on: ${{ vars.PIPELINE_RUNNER }}
engine: copilot

imports:
  - .github/agents/pipeline-review.agent.md

permissions:
  contents: read
  issues: read
  pull-requests: read

tools:
  github:
    toolsets: [default]

safe-outputs:
  create-pull-request-review-comment:
    max: 15
  submit-pull-request-review:
    max: 1
    allowed-events: [COMMENT, REQUEST_CHANGES, APPROVE]
  add-comment:
    max: 2
    target: "*"
  add-labels:
    allowed: ["cloud/review", "cloud/awaiting-merge"]
    max: 1
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  remove-labels:
    allowed: ["cloud/triage", "cloud/planning", "cloud/implementing", "cloud/review", "cloud/awaiting-merge", "cloud/documenting", "cloud/summarizing", "cloud/done"]
    max: 9
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  dispatch-workflow: [cloud-cyberpilot-docs, cloud-cyberpilot-implement]
---

## Pipeline — Review Agent

Run the imported `pipeline-review` agent instructions as the review policy for the cloud AI-SDLC pipeline.

**Target issue:** #${{ github.event.inputs.issue_number }}

## Cloud Duties

1. Find the PR linked to issue #${{ github.event.inputs.issue_number }} using issue timeline cross-reference events first, then closing-keyword body/title search as fallback.
2. Remove existing `cloud/*` labels and add `cloud/review`.
3. Post one `## 🔄 Pipeline — Review In Progress` status comment with the PR number.
4. Review the PR using the imported review policy and specialist-review expectations.
5. Submit exactly one PR review with verdict `APPROVE`, `REQUEST_CHANGES`, or `COMMENT`.
6. Post one issue comment headed `## 🏗️ Pipeline — Implementation Report` with PR, branch, verdict, change summary, and review summary.
7. Dispatch the next stage according to the cloud dispatch chain below.

## Cloud Dispatch Chain

- `APPROVE` or `COMMENT`: replace `cloud/review` with `cloud/awaiting-merge`, then dispatch `cloud-docs` with `issue_number` set to `${{ github.event.inputs.issue_number }}`. Docs will then hand off to `cloud-summary` before finish.
- `REQUEST_CHANGES`: keep `cloud/review`, count previous request-changes implementation reports, and dispatch `cloud-implement` only if fewer than 2 rework cycles have already occurred.
- If 2 rework cycles have already occurred, post a halt comment and do not dispatch another workflow.

## Cloud Overrides

- Cloud PR discovery, label transitions, review submission limits, and workflow dispatch rules in this file override local-only instructions in the imported review agent.
- Keep PR discovery aligned with `cloud-finish.yml`: timeline linkage first, keyword scans second.
- The implementation report is mandatory because it is the official issue-level pipeline record.
