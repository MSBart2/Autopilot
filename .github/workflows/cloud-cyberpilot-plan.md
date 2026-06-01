---
name: "Cyberpilot — Plan"
description: "Creates a detailed implementation plan and saves it to the issue"

on:
  workflow_dispatch:
    inputs:
      issue_number:
        description: "Issue number to plan"
        required: true
        type: string

runs-on: ${{ vars.PIPELINE_RUNNER }}
engine:
  id: copilot
  model: claude-opus-4.6

imports:
  - .github/agents/plan.agent.md

permissions:
  contents: read
  issues: read
  pull-requests: read

tools:
  github:
    toolsets: [default]
  cache-memory: true

safe-outputs:
  add-comment:
    max: 2
    target: "*"
  update-issue:
    body:
    target: "*"
    max: 1
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  add-labels:
    allowed: ["cloud/planning", "cloud/implementing"]
    max: 1
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  remove-labels:
    allowed: ["cloud/triage", "cloud/planning", "cloud/implementing", "cloud/review", "cloud/awaiting-merge", "cloud/done"]
    max: 7
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  dispatch-workflow: [cloud-cyberpilot-implement]
---

## Pipeline — Plan Agent

Run the imported `plan` agent instructions as the planning policy for the cloud AI-SDLC pipeline.

**Target issue:** #${{ github.event.inputs.issue_number }}

## Cloud Duties

1. Remove existing `cloud/*` labels and add `cloud/planning`.
2. Read issue #${{ github.event.inputs.issue_number }}, including the `Pipeline — Triage` handoff comment.
3. Use the imported plan agent to research the codebase and create the implementation plan.
4. Post one plan comment headed `## 📋 Pipeline — Plan` with branch, implementation steps, agent delegation, acceptance criteria, and testing requirements.
5. Replace `cloud/planning` with `cloud/implementing`.
6. Dispatch `cloud-implement` with `issue_number` set to `${{ github.event.inputs.issue_number }}`.

## Cloud Overrides

- Cloud labels, branch naming, issue comment headings, and workflow dispatch rules in this file override local-only instructions in the imported plan agent.
- The plan must be specific enough for the asynchronous Copilot coding agent assigned by `cloud-implement` to execute without ambiguity.
- Documentation and tests must be represented in the plan even though docs run as a separate cloud stage after review.
- If the plan cannot be fully determined, post the best known plan and still dispatch `cloud-implement`.
