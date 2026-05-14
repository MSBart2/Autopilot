# AI-Driven Software Development Lifecycle

Cyberpilot is a pipeline-first AI-driven software development lifecycle (AI-SDLC) repository. The same issue-driven workflow can run three ways:

- **Local mode:** VS Code Copilot Chat or Copilot CLI delegates to repository agents.
- **Cloud mode:** GitHub Agentic Workflows dispatch stages on GitHub Actions runners.
- **SDK mode:** a .NET console runner drives the same agent prompts through the Copilot SDK.

All modes use the GitHub issue as the state file. Each stage posts structured comments so downstream stages can continue from visible, auditable handoffs.

## Table of Contents

- [Shared Pipeline Model](#shared-pipeline-model)
- [Agent Roster](#agent-roster)
- [Mode Comparison](#mode-comparison)
- [Local Mode](#local-mode)
- [Cloud Mode](#cloud-mode)
- [SDK Mode](#sdk-mode)
- [Labels](#labels)
- [Failure Handling](#failure-handling)
- [Workflow Maintenance](#workflow-maintenance)
- [References](#references)

---

## Shared Pipeline Model

```text
Issue -> Triage -> Plan -> Implement -> Review -> Docs -> Deliver
```

The default stage order is shared across local and SDK mode. Cloud mode uses the same logical flow, with a final `Finish` workflow that performs delivery on GitHub Actions. SDK mode can also run selected pipeline definitions, such as focused `bugfix` and `docs-only` variants, while preserving the default full SDLC as `cyberpilot-default`.

| Stage | Purpose | Primary Artifact |
|-------|---------|------------------|
| Triage | Classify the issue, check duplicates, decide whether the issue can proceed | Triage issue comment |
| Plan | Research the codebase and produce a file-level implementation plan | Plan issue comment and feature branch |
| Implement | Apply the plan, run tests, and open a PR | Commits and pull request |
| Review | Review architecture, security, quality, tests, docs, and build health | PR review verdict |
| Docs | Update XML comments, Markdown docs, and human verification notes | Documentation commit and issue comment |
| Deliver | Merge the approved PR and record completion | Merge, branch cleanup, landing report |

The issue thread is the handoff channel. Stage agents read prior comments instead of relying on hidden state.

### Review Loop

If review requests changes, the pipeline loops back to implementation. The current policy allows up to two review cycles before halting for human intervention.

---

## Agent Roster

The agent files live in [.github/agents/](.github/agents). Each file includes a `Pipeline Placement` section that declares whether it is a stage, specialist, reviewer, quality gate, or orchestrator.

### Orchestrator and Stages

| Agent | Role | Phase | File |
|-------|------|-------|------|
| `cyberpilot` | Orchestrator | all | [.github/agents/cyberpilot.agent.md](.github/agents/cyberpilot.agent.md) |
| `triage` | Stage | triage | [.github/agents/triage.agent.md](.github/agents/triage.agent.md) |
| `plan` | Stage | planning | [.github/agents/plan.agent.md](.github/agents/plan.agent.md) |
| `implement` | Stage | implementation | [.github/agents/implement.agent.md](.github/agents/implement.agent.md) |
| `pipeline-review` | Stage | review | [.github/agents/pipeline-review.agent.md](.github/agents/pipeline-review.agent.md) |
| `docs` | Stage | documentation | [.github/agents/docs.agent.md](.github/agents/docs.agent.md) |
| `deliver` | Stage | delivery | [.github/agents/deliver.agent.md](.github/agents/deliver.agent.md) |

### Specialists and Quality Gates

| Agent | Role | Used By | File |
|-------|------|---------|------|
| `backend` | Specialist | `plan`, `implement`, `testing` | [.github/agents/backend.agent.md](.github/agents/backend.agent.md) |
| `frontend` | Specialist | `plan`, `implement` | [.github/agents/frontend.agent.md](.github/agents/frontend.agent.md) |
| `security-implementer` | Specialist | `plan`, `implement`, `testing` | [.github/agents/security-implementer.agent.md](.github/agents/security-implementer.agent.md) |
| `testing` | Specialist | `implement` | [.github/agents/testing.agent.md](.github/agents/testing.agent.md) |
| `build-validator` | Quality gate | `implement`, `pipeline-review`, `code-quality-reviewer` | [.github/agents/build-validator.agent.md](.github/agents/build-validator.agent.md) |
| `code-quality-reviewer` | Specialist reviewer | `pipeline-review`, `docs` | [.github/agents/code-quality-reviewer.agent.md](.github/agents/code-quality-reviewer.agent.md) |
| `security-reviewer` | Specialist reviewer | `pipeline-review`, `build-validator`, `code-quality-reviewer`, `docs` | [.github/agents/security-reviewer.agent.md](.github/agents/security-reviewer.agent.md) |

---

## Mode Comparison

| Capability | Local | Cloud | SDK |
|------------|-------|-------|-----|
| Entry point | VS Code Copilot Chat or Copilot CLI | GitHub label or workflow dispatch | .NET console app |
| Orchestration | `cyberpilot.agent.md` | `cloud-cyberpilot*.md` workflows | `SdkCyberpilotRunner` |
| Stage prompts | `.github/agents/*.agent.md` | Workflow prompts plus imported agents | `.github/agents/*.agent.md` with SDK wrapper |
| Labels | `local`, `local/*` | `cyberpilot`, `cloud/*` | `sdk`, `sdk/*` |
| Runtime | Current editor/chat session | GitHub Actions + Copilot coding agent | Copilot SDK sessions |
| Process variants | Manual single-stage invocation | Workflow-specific dispatch | Built-in or JSON-backed pipeline definitions |
| Best for | Hands-on local delivery | Remote autonomous delivery | Programmatic experiments and repeatable controller behavior |

---

## Local Mode

Local mode runs from VS Code Copilot Chat or Copilot CLI. The `cyberpilot` agent is the normal entry point and delegates to each stage agent in sequence.

### Invoke the Full Pipeline

```text
@cyberpilot run issue 135
```

Copilot CLI equivalent:

```bash
copilot "@cyberpilot run issue 135"
```

### Advanced Single-Stage Invocation

Use these only when resuming or debugging a pipeline stage:

```bash
copilot "@triage triage issue 135"
copilot "@plan plan issue 135"
copilot "@implement implement issue 135"
copilot "@pipeline-review review PR 142"
copilot "@docs document issue 135"
copilot "@deliver deliver PR 142"
```

### Local Stage Behavior

| Stage | Agent | Subagents | Output |
|-------|-------|-----------|--------|
| Triage | `triage` | none | classification comment and labels |
| Plan | `plan` | `backend`, `frontend`, `security-implementer` | detailed plan and branch |
| Implement | `implement` | `backend`, `frontend`, `security-implementer`, `testing`, `build-validator` | code, tests, PR |
| Review | `pipeline-review` | `security-reviewer`, `code-quality-reviewer`, `build-validator` | PR review verdict |
| Docs | `docs` | `code-quality-reviewer`, `security-reviewer` | documentation and verification notes |
| Deliver | `deliver` | none | merge and landing report |

### Local Labels

The `cyberpilot` agent owns all `local/*` label transitions. Stage agents do not set pipeline labels.

| Label | Meaning |
|-------|---------|
| `local` | Persistent provenance marker for issues handled by local mode |
| `local/triage` | Triage in progress |
| `local/planning` | Planning in progress |
| `local/implementing` | Implementation in progress |
| `local/review` | PR review in progress |
| `local/docs` | Documentation update in progress |
| `local/delivering` | Delivery in progress |
| `local/done` | Local pipeline complete |

### Local Prerequisites

- Copilot CLI or VS Code Copilot Chat
- GitHub CLI (`gh`) authenticated
- Git push access to the repository
- .NET SDK used by the repository

---

## Cloud Mode

Cloud mode uses GitHub Agentic Workflows and GitHub Actions. It is the remote automation lane for issue-to-PR delivery.

### Trigger the Cloud Pipeline

Apply the `cloud/cyberpilot` label to an issue, or run the cloud cyberpilot workflow manually.

You can also apply `cloud/triage-requested` directly to start at triage.

### Cloud Workflow Files

| File | Purpose | Trigger |
|------|---------|---------|
| `.github/workflows/cloud-cyberpilot.md` | Single entry point, validates and dispatches | `cloud/cyberpilot` or dispatch |
| `.github/workflows/cloud-cyberpilot-triage.md` | Classify and dispatch | `cloud/triage-requested` |
| `.github/workflows/cloud-cyberpilot-plan.md` | Create implementation plan | workflow dispatch |
| `.github/workflows/cloud-cyberpilot-implement.md` | Assign Copilot coding agent | workflow dispatch |
| `.github/workflows/cloud-cyberpilot-review.md` | Multi-agent code review and issue report | `cloud/review` or dispatch |
| `.github/workflows/cloud-cyberpilot-docs.md` | Add XML docs and update Markdown | workflow dispatch |
| `.github/workflows/cloud-finish.yml` | Squash merge PR, delete branch, close issue | workflow dispatch |

Agentic workflow sources are Markdown files compiled to lockfiles with `gh aw compile`. `cloud-finish.yml` is plain GitHub Actions YAML.

### Cloud Human Touchpoints

Cloud mode is mostly automatic, but two gates are expected:

1. After Copilot coding agent creates a PR, apply `cloud/review` to the issue to resume review.
2. Approve the review workflow run when GitHub requires first-time workflow approval.

The resume label exists because GitHub's anti-recursion rule prevents workflows triggered by Copilot's GitHub App token from automatically triggering downstream pull-request workflows.

### Cloud Labels

| Label | Meaning |
|-------|---------|
| `cyberpilot` | Persistent provenance marker for issues handled by cloud mode |
| `cloud/cyberpilot` | Reusable trigger label |
| `cloud/triage-requested` | Triage trigger |
| `cloud/triage` | Triage in progress |
| `cloud/planning` | Planning in progress |
| `cloud/implementing` | Copilot coding agent assigned |
| `cloud/review` | Review requested or in progress |
| `cloud/awaiting-merge` | Review approved, waiting for docs/finish |
| `cloud/documenting` | Documentation in progress |
| `cloud/done` | Cloud pipeline complete |

### Cloud End-to-End Flow

```text
1. Apply `cloud/cyberpilot` to an issue
2. Cyberpilot validates and dispatches triage
3. Triage classifies and dispatches plan
4. Plan posts the implementation plan and dispatches implement
5. Implement assigns Copilot coding agent
6. Copilot coding agent creates the PR
7. Apply `cloud/review` to resume the pipeline
8. Review approves or dispatches rework
9. Docs updates documentation after approval
10. Finish merges the PR, deletes the branch, and closes the issue
```

---

## SDK Mode

SDK mode is the programmatic lane. It runs as a .NET console app under [copilot-sdk-exe/](copilot-sdk-exe) and drives the same agent prompts through the Copilot SDK.

SDK mode is experimental. It is useful for testing prompt contracts, runner behavior, model availability, label handling, and how much of the AI-SDLC controller can live in regular application code.

### Invoke SDK Mode

From the repository root:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- issue 135 --repo rbmathis/Cyberpilot --approve-all --skip-deliver
```

Useful preflights:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --ensure-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-model --repo rbmathis/Cyberpilot
```

The EXE can read the same repo/token configuration used by the web dashboard. It automatically checks `appsettings.json` and `appsettings.Development.json` in the current directory and under `web/`, or you can pass a file explicitly:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- issue 135 --repo rbmathis/Cyberpilot --config .\web\appsettings.json --approve-all --skip-deliver
```

When `--repo` is omitted, the EXE uses `Cyberpilot:Repository` or the first configured repository. Matching configured tokens take precedence for that repository; otherwise the EXE falls back to `GITHUB_TOKEN` or `GH_TOKEN`.

The web dashboard can also launch SDK mode. The issue launcher accepts a GitHub repository as `owner/name`, `https://github.com/owner/name`, or `git@github.com:owner/name.git`, plus a GitHub token with access to that repository. The token is stored only in the web app memory cache behind a short-lived connection id and is passed to the queued SDK run; it is not persisted in pipeline history or posted back in issue forms.

The launcher can preload repo/token pairs from configuration:

```json
"Cyberpilot": {
	"Repositories": [
		{
			"Name": "Cyberpilot",
			"Repository": "rbmathis/Cyberpilot",
			"RepoRoot": "..",
			"Token": ""
		}
	],
	"AgentPromptRoot": ".."
}
```

`RepoRoot` is the target local clone path where SDK work runs. If the path is missing, the web runner clones the configured GitHub repository there using the configured token, validates that the result is writable and is a git work tree, then lets the SDK create or switch issue branches inside that clone.

`AgentPromptRoot` is the controller repo that contains `.github/agents`. This lets Cyberpilot run against another repository, such as `MSBart2/Nonograms`, while still loading its stage instructions from this Cyberpilot repository.

### Controller Repo vs Target Repo

Web-triggered SDK mode deliberately separates the orchestration repository from the repository being changed:

| Setting | Meaning | Example |
|---------|---------|---------|
| `AgentPromptRoot` | Controller repository that owns `.github/agents/*.agent.md` and the AI-SDLC instructions | `C:\Users\rdpuser\Source\Cyberpilot` |
| `RepoRoot` | Target repository clone where branches, edits, tests, commits, and PRs happen | `C:\Users\rdpuser\Source\Nonograms` |
| `Repository` | GitHub repository whose issues, labels, comments, branches, and PRs are managed | `MSBart2/Nonograms` |

This means a single Cyberpilot installation can act as an AI-SDLC controller for many repositories. The target repositories do not need to copy this repo's `.github/agents` folder. They need GitHub access, a configured clone target, and whatever build/test tooling their own code requires.

At runtime, the SDK builds each stage prompt from `AgentPromptRoot/.github/agents/<stage>.agent.md`, then tells Copilot that `RepoRoot` is the repository root to inspect and modify. That keeps prompt governance centralized while allowing execution to happen in repo-specific local clones.

Web-triggered SDK runs set `Cyberpilot:EnsureLabels` to `true` by default, so newly configured repositories get the required `sdk/*` labels created before triage starts.

For local development, prefer user secrets or environment variables for real token values, such as `Cyberpilot__Repositories__0__Token`, instead of committing tokens to appsettings files.

### SDK Safety Gates

- Checks required `sdk/*` labels before running stages.
- Checks Copilot model availability before mutating issue labels.
- Exits without mutation for closed issues.
- Requires `--approve-all` before approving Copilot SDK tool permissions.
- Uses per-stage timeouts.
- Parses final stage results and fails closed when expected structured output is missing.
- Supports `--skip-deliver` for pilot runs that stop before merge.
- Supports `--allow-missing-docs` when deliberately accepting missing documentation risk.

### SDK Pipeline Definitions And Policies

SDK mode selects a pipeline definition and policy profile before issue, label, model, or stage side effects begin. Built-in definitions include:

| Definition | Stages | Use case |
|------------|--------|----------|
| `cyberpilot-default` | `triage -> plan -> implement -> review -> docs -> deliver` | Full issue-to-PR SDLC |
| `bugfix` | `plan -> implement -> review -> deliver` | Focused fixes when triage and docs are unnecessary |
| `docs-only` | `docs -> deliver` | Documentation-only updates and landing reports |

The SDK executable supports JSON-backed definitions with `--pipeline-definition-file <path>`. File-backed definitions are combined with built-ins and validated before routing starts. The web launcher exposes built-in definitions only.

Policy profiles are `lenient`, `standard`, `strict`, and `security-critical`. The selected profile is recorded on the run, injected into SDK prompts, and used by policy-aware validation and evidence. See [docs/policies.md](docs/policies.md) for profile behavior and compatibility notes.

### SDK Evidence And Approvals

SDK runs persist structured evidence separately from raw stage transcripts. Evidence can include stage artifacts, policy rationale, deterministic gate outcomes, approval decisions, pull request references, token/cost usage, and repository profile signals.

Web-triggered SDK runs can pause after a stage and create a first-class approval request. Operators can approve or reject in the Run Room; approved requests resume from the recorded resume stage, while rejected requests stop the run until targeted retry or rework addresses the rejection. See [docs/approval-workflow.md](docs/approval-workflow.md) for the operator workflow.

### SDK Labels

| Label | Meaning |
|-------|---------|
| `sdk` | Persistent provenance marker for SDK mode |
| `sdk/triage` | Triage in progress |
| `sdk/planning` | Planning in progress |
| `sdk/implementing` | Implementation in progress |
| `sdk/review` | Review in progress |
| `sdk/docs` | Documentation in progress |
| `sdk/delivering` | Delivery in progress |
| `sdk/done` | SDK pipeline complete |
| `sdk/failed` | SDK pipeline halted |

---

## Labels

Each mode has an isolated label namespace so local, cloud, and SDK runs do not collide:

| Mode | Provenance Label | Stage Namespace |
|------|------------------|-----------------|
| Local | `local` | `local/*` |
| Cloud | `cyberpilot` | `cloud/*` |
| SDK | `sdk` | `sdk/*` |

Classification labels such as `bug`, `enhancement`, `feature`, `security`, `documentation`, and `refactor` are shared and describe the issue type rather than pipeline state.

---

## Failure Handling

Failures are reported on the issue whenever possible. The issue retains the stage comments already posted, which gives the next run or a human operator an audit trail.

| Mode | Failure Behavior |
|------|------------------|
| Local | `cyberpilot` stops and reports the failed stage. Re-run the full pipeline or resume from a specific agent. |
| Cloud | Workflow failure comments and Actions logs provide telemetry. Re-run from the Actions tab or reapply the relevant trigger label. |
| SDK | Runner fails closed, records stage results and evidence, uses `sdk/failed` for halted runs, and treats approvals as resumable pauses rather than failures. |

Review rework is capped at two cycles before human intervention.

---

## Workflow Maintenance

When editing `.github/workflows/cloud-*.md`, delete existing cloud lockfiles before compiling:

```powershell
Remove-Item .github/workflows/cloud-*.lock.yml -ErrorAction SilentlyContinue
gh aw compile
```

Commit regenerated `.lock.yml` files with their `.md` sources. Do not edit lockfiles by hand.

---

## References

- [.github/agents/README.md](.github/agents/README.md) - custom agent details
- [copilot-sdk/README.md](copilot-sdk/README.md) - SDK library notes
- [copilot-sdk-exe/README.md](copilot-sdk-exe/README.md) - SDK executable harness
- [architecture.md](architecture.md) - technical architecture
- [docs/README.md](docs/README.md) - operational documentation index
- [docs/policies.md](docs/policies.md) - policy profiles and evidence behavior
- [docs/approval-workflow.md](docs/approval-workflow.md) - approval pause and resume workflow