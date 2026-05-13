---
name: "Cyberpilot — Implement"
description: "Reads the plan from the issue and assigns Copilot coding agent"

on:
  workflow_dispatch:
    inputs:
      issue_number:
        description: "Issue number to implement"
        required: true
        type: string

runs-on: ${{ vars.PIPELINE_RUNNER }}
engine: copilot

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
    max: 1
    target: "*"
  add-labels:
    allowed: ["cloud/implementing"]
    max: 1
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  remove-labels:
    allowed: ["cloud/triage", "cloud/planning", "cloud/implementing", "cloud/review", "cloud/awaiting-merge", "cloud/done"]
    max: 7
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  assign-to-agent:
    name: "copilot"
    target: "*"
    github-token: ${{ secrets.COPILOT_GITHUB_TOKEN }}
    custom-instructions: |
      ## YOUR MISSION

      Implement the plan from issue #${{ github.event.inputs.issue_number }}.

      ## STEP 0: READ THE PLAN (do this first)

      Read issue #${{ github.event.inputs.issue_number }} and find the comment titled "📋 Pipeline — Plan". That comment has your implementation plan with specific files, changes, and acceptance criteria. Follow it precisely.

      ## STEP 1: IMPLEMENT

      Use the specialized sub-agents in .github/agents/:
      - `backend` agent for Controllers, Models, Services, Middleware, Program.cs
      - `frontend` agent for Views, Razor templates, CSS, JavaScript
      - `security-implementer` agent for security vulnerabilities (CSRF, XSS, SQL injection, auth)

      ## STEP 2: TEST (mandatory — do not skip)

      Use `testing` agent to generate unit tests and integration tests for ALL changes.

      ## STEP 3: DOCUMENT (mandatory — do not skip)

      Use `docs` agent to:
      - Add/update XML documentation on all new/modified public methods and classes
      - Update docs/ markdown files if the change affects architecture or APIs

      ## STEP 4: VALIDATE

      Use `build-validator` agent to verify the project builds cleanly and tests pass.

      ## STEP 5: COMMIT AND PR WITH PERSONALITY

      Follow the spirit of `.github/skills/snarky-commit/SKILL.md` even if you cannot invoke the skill directly from the cloud coding-agent environment.

      **Commit messages:**
      - Use conventional commit format when practical, but make the description vivid and specific.
      - Describe the real change with a little swagger, not generic filler like `update files`.
      - Keep the subject concise and PG-13.

      **PR title:**
      - Generate a spicy, confident PR title based on the actual changes and the issue title.
      - Do not reuse the issue title verbatim unless it is already interesting.
      - One emoji is fine; three is trying too hard.

      **PR body:**
      - Use the snarky PR body shape: `What's This?`, `Quality Checks`, `Commit Messages`, and `Side Effects` when relevant.
      - Keep the tone playful but still useful to reviewers.
      - Preserve every required pipeline line below, especially the closing keyword and automated-label behavior.

      ## CRITICAL: PR BODY REQUIREMENT

      When creating the pull request, the PR body MUST include a closing keyword line:

      `
      Closes #${{ github.event.inputs.issue_number }}
      `

      This line is required for the pipeline to automatically detect the linked issue and trigger the review stage. Do not omit it.

      ## CRITICAL: PR LABEL REQUIREMENT

      When creating the pull request, apply the `automated` label to the PR.

      That label is required because later pipeline stages are only allowed to push follow-up documentation commits to PR branches explicitly marked as pipeline-managed.

      NOTE: Do NOT post an implementation report on the issue. The review pipeline handles that automatically.
---

## Pipeline — Implement Agent

You are the implementation coordinator. You read the plan from the issue and assign Copilot coding agent to execute it.

**Target issue:** #${{ github.event.inputs.issue_number }}

## Your Task

1. **Read the issue** (#${{ github.event.inputs.issue_number }}) — title, body, and all comments
2. **Ensure `cloud/implementing` label is on the issue** (remove any other `cloud/*` labels)
3. **Find the plan comment** — look for the comment containing "Pipeline — Plan" with the implementation steps
4. **Post a status comment** confirming you're assigning the coding agent
5. **Assign Copilot coding agent** to issue #${{ github.event.inputs.issue_number }}

## Status Comment Format

Post this on issue #${{ github.event.inputs.issue_number }}:

```markdown
## 🚀 Pipeline — Implement

**Timestamp:** [UTC time]
**Status:** Copilot coding agent assigned

The plan has been reviewed and the coding agent has been assigned to execute the implementation.

### ⏸️ Pipeline paused — waiting for Copilot to finish

The coding agent works asynchronously. When it finishes, a PR will be created and you will receive a notification that the PR is ready for review.

**To resume the pipeline:** apply the `cloud/review` label to this issue. That will trigger the automated review stage, and the rest of the pipeline (review → docs → merge → close) will run automatically.
```

## Important

- Always assign the agent to the correct issue number: ${{ github.event.inputs.issue_number }}
- The custom-instructions already tell the coding agent to read the plan from the issue
- If the plan comment is missing, still assign the agent — it will figure it out from the issue body
- If you cannot assign the agent, call `noop` with an explanation
