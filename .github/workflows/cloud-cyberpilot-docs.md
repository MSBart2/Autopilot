---
name: "Cyberpilot — Docs"
description: "Documents the implemented changes after review approval"

on:
  workflow_dispatch:
    inputs:
      issue_number:
        description: "Issue number to document"
        required: true
        type: string

runs-on: ${{ vars.PIPELINE_RUNNER }}
engine: copilot

imports:
  - .github/agents/docs.agent.md

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
    target: "*"
  add-labels:
    allowed: ["cloud/documenting", "cloud/summarizing"]
    max: 1
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  remove-labels:
    allowed: ["cloud/triage", "cloud/planning", "cloud/implementing", "cloud/review", "cloud/awaiting-merge", "cloud/documenting", "cloud/summarizing", "cloud/done"]
    max: 9
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  create-pull-request-review-comment:
    max: 5
  push-to-pull-request-branch:
    max: 1
    target: "*"
    labels: [automated]
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  dispatch-workflow: [cloud-cyberpilot-summary]
---

## Pipeline — Docs Agent

Run the imported `docs` agent instructions as the documentation policy for the cloud AI-SDLC pipeline.

**Target issue:** #${{ github.event.inputs.issue_number }}

## Cloud Duties

1. Find the PR linked to issue #${{ github.event.inputs.issue_number }} using issue timeline cross-reference events first, then closing-keyword body/title search as fallback.
2. Remove existing `cloud/*` labels and add `cloud/documenting`.
3. Read the PR diff and update XML/Markdown documentation using the imported docs policy.
4. Push documentation-only changes to the PR branch only when the PR has the `automated` label.
5. Post one issue comment headed `## 📚 Pipeline — Documentation` with PR, files documented, verification notes, and summary.
6. Dispatch `cloud-summary` with `issue_number` set to `${{ github.event.inputs.issue_number }}`.

## Cloud Overrides

- Cloud PR discovery, label transitions, branch-push restrictions, and workflow dispatch rules in this file override local-only instructions in the imported docs agent.
- Documentation is non-blocking; if documentation cannot be completed, explain the gap in the issue comment and still dispatch `cloud-summary`.
- Never modify implementation logic in this stage.
