---
name: "Cyberpilot — Summary"
description: "Generates the stakeholder-ready summary and changelog package"

on:
  workflow_dispatch:
    inputs:
      issue_number:
        description: "Issue number to summarize"
        required: true
        type: string

runs-on: ${{ vars.PIPELINE_RUNNER }}
engine: copilot

imports:
  - .github/agents/summary.agent.md

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
    allowed: ["cloud/summarizing"]
    max: 1
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  remove-labels:
    allowed: ["cloud/triage", "cloud/planning", "cloud/implementing", "cloud/review", "cloud/awaiting-merge", "cloud/documenting", "cloud/summarizing", "cloud/done"]
    max: 9
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  dispatch-workflow: [cloud-finish]
---

## Pipeline — Summary Agent

Run the imported `summary` agent instructions as the summary/changelog policy for the cloud AI-SDLC pipeline.

**Target issue:** #${{ github.event.inputs.issue_number }}

## Cloud Duties

1. Find the PR linked to issue #${{ github.event.inputs.issue_number }} using issue timeline cross-reference events first, then closing-keyword body/title search as fallback.
2. Remove existing `cloud/*` labels and add `cloud/summarizing`.
3. Read the issue, implementation, review, docs, and PR context needed to explain the change in plain English.
4. Post one issue comment headed `## 🧾 Summary & Changelog` with what changed, why, affected components, breaking changes, migration notes, and rollback notes.
5. Dispatch `cloud-finish` with `issue_number` set to `${{ github.event.inputs.issue_number }}`.

## Cloud Overrides

- Cloud PR discovery, label transitions, and workflow dispatch rules in this file override local-only instructions in the imported summary agent.
- If the summary cannot be fully completed, explain the gap clearly in the issue comment and still dispatch `cloud-finish`.
- Never merge the PR or edit lockfiles by hand from this stage.
