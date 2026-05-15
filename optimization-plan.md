# Cyberpilot Optimization Plan

## Purpose

Cyberpilot is becoming a custom harness around GitHub Copilot SDK sessions, not just a wrapper that fires one prompt per pipeline stage. The next optimization pass should make that harness cheaper, faster, more observable, easier to steer, and less dependent on model-discovered workflow state.

This plan is intentionally design-focused. Do not execute it until the team has reviewed the tradeoffs and sequencing.

## SDK references reviewed

Primary references:

- GitHub Docs: [Getting started with Copilot SDK](https://docs.github.com/en/copilot/how-tos/copilot-sdk/sdk-getting-started)
- SDK repo docs: [GitHub Copilot SDK documentation](https://github.com/github/copilot-sdk/blob/main/docs/index.md)
- SDK repo docs: [Build your first Copilot-powered app](https://github.com/github/copilot-sdk/blob/main/docs/getting-started.md)
- SDK repo docs: [Agent loop](https://github.com/github/copilot-sdk/blob/main/docs/features/agent-loop.md)
- SDK repo docs: [Streaming session events](https://github.com/github/copilot-sdk/blob/main/docs/features/streaming-events.md)
- SDK repo docs: [Steering and queueing](https://github.com/github/copilot-sdk/blob/main/docs/features/steering-and-queueing.md)
- SDK repo docs: [Session persistence](https://github.com/github/copilot-sdk/blob/main/docs/features/session-persistence.md)
- SDK repo docs: [Custom agents and sub-agent orchestration](https://github.com/github/copilot-sdk/blob/main/docs/features/custom-agents.md)
- SDK repo docs: [Custom skills](https://github.com/github/copilot-sdk/blob/main/docs/features/skills.md)
- SDK repo docs: [MCP servers](https://github.com/github/copilot-sdk/blob/main/docs/features/mcp.md)
- SDK repo docs: [Pre-tool use hook](https://github.com/github/copilot-sdk/blob/main/docs/hooks/pre-tool-use.md)
- SDK repo docs: [Post-tool use hook](https://github.com/github/copilot-sdk/blob/main/docs/hooks/post-tool-use.md)
- SDK repo docs: [OpenTelemetry instrumentation](https://github.com/github/copilot-sdk/blob/main/docs/observability/opentelemetry.md)

Follow-up documentation task:

- Add a repo-local SDK reference file, likely `docs/copilot-sdk-references.md`, summarizing supported SDK features and links.
- Add a short lookup note to `AGENTS.md` pointing future agents to that reference before changing SDK integration code.

## Key SDK capabilities we should design around

| Capability | Why it matters for Cyberpilot |
| --- | --- |
| Explicit sessions | We can treat each pipeline run, stage, or review dimension as a managed session with known lifecycle and cleanup. |
| Per-session model selection | Enables tiered models by stage and safer model fallback. |
| Streaming events | Lets the dashboard show real progress, token usage, tool execution, errors, and idle/completion state. |
| `assistant.usage` events | Gives per-call input/output tokens, cache tokens, cost multiplier, duration, provider IDs, and quota snapshots. This is better than only reading final usage metrics. |
| `assistant.turn_start` / `assistant.turn_end` | Turn count equals LLM API calls. We can measure model loop inefficiency directly. |
| `session.idle` | Reliable mechanical completion signal; better than depending only on semantic task completion. |
| Session persistence and resume | Enables crash recovery, pausing, human intervention, and continued work without reconstructing all context. |
| Steering and queueing | Enables human intervention while a stage is still running, without killing the session. |
| Custom tools | Lets us provide cheap deterministic operations for GitHub, git, diff, validation, policy checks, and state-store writes instead of asking the model to rediscover them. |
| Pre-tool hooks | Can enforce permissions, normalize arguments, inject context, suppress verbose output, and prevent risky or wasteful tool calls. |
| Post-tool hooks | Can redact secrets, truncate/summarize noisy output, audit tool execution, and inject helpful failure context. |
| Custom agents | Lets us define security, quality, docs, build, and delivery agents with scoped prompts and tool access. |
| Skills | Lets us package domain instructions and preload only the skills each agent actually needs. |
| MCP servers | Gives a standard path for richer GitHub, database, filesystem, browser, and metrics integrations. |
| OpenTelemetry | Lets us trace SDK-to-CLI-to-tool execution with provider request IDs and stage/run correlation. |

## Current harness observations

- `CopilotStageRunner` creates a new `CopilotClient` and session for each stage.
- `CopilotStageRunner` already enables streaming and captures streamed deltas.
- Usage capture currently reads final RPC usage metrics after `SendAndWaitAsync`, but the SDK exposes richer `assistant.usage` events during streaming.
- `PromptBuilder` builds one large prompt wrapper per stage and imports the stage agent prompt.
- `StageExecutor` validates artifacts after the model finishes, but pre-stage gates can still be improved to avoid agent spin-up when deterministic state is missing.
- `PipelineExecutionContext` is small today: options, definition, final stage, branch, PR URL, and stage results. It is the natural place for run-scoped cache and SDK/session metadata.
- Stage agent prompts currently ask models to perform discovery that the harness often already knows, such as PR routing information.

## Optimization principles

1. **Move deterministic work out of the model.** If the harness can know it via API, database, git, or configuration, make it a tool/gate/context field rather than prose the model must rediscover.
2. **Measure every model turn.** Optimize from token count, turn count, duration, retries, and tool calls, not vibes.
3. **Use smaller context and smaller models by default.** Escalate only when stage complexity requires it.
4. **Make human intervention first-class.** Pause, steer, resume, and inspect sessions without corrupting pipeline state.
5. **Prefer typed contracts over prompt conventions.** Use custom tools and structured artifacts to reduce markdown parsing fragility.
6. **Keep isolation clear.** Each pipeline run, repository, PR, and user intervention needs explicit ownership and session identity.

## Recommended roadmap

### Phase 0: SDK reference and decision log

Goal: make SDK integration choices repeatable for future agents.

Work:

- Create `docs/copilot-sdk-references.md` with the SDK links above, key supported features, and Cyberpilot-specific integration notes.
- Update `AGENTS.md` to point SDK-related work to that reference.
- Add a short "SDK harness decisions" section for why we use sessions per stage, how we handle streaming, and where future agents should add hooks/tools.

Why first:

- This prevents the harness from drifting as different agents rediscover SDK behavior.
- It gives us a stable place to track preview-feature caveats.

### Phase 1: Observability-first instrumentation

Goal: make optimization measurable before changing behavior.

Work:

- Subscribe to richer streaming events in `CopilotStageRunner`:
  - `assistant.turn_start`
  - `assistant.turn_end`
  - `assistant.usage`
  - `tool.execution_start`
  - `tool.execution_complete`
  - `session.error`
  - `session.idle`
- Extend `StageResult` or add a `StageExecutionMetrics` model with:
  - model
  - input tokens
  - output tokens
  - cache read/write tokens
  - estimated cost multiplier
  - duration
  - turn count
  - tool call count
  - failed tool call count
  - provider call IDs / request IDs where available
- Persist metrics to the existing run/stage state store and surface them in the dashboard.
- Add optional OpenTelemetry config for SDK traces using `TelemetryConfig`.

Target files:

- `copilot-sdk/Copilot/CopilotStageRunner.cs`
- `copilot-sdk/Pipeline/StageResult.cs`
- `copilot-sdk/Pipeline/PipelineExecutionContext.cs`
- Web run/stage persistence models and dashboard views

Success criteria:

- Every stage reports token usage, turn count, tool count, duration, and errors.
- The dashboard can show which stages are expensive or looping.
- We can compare before/after improvements quantitatively.

### Phase 2: Harness-owned state and context injection

Goal: stop asking the model to rediscover known workflow facts.

Work:

- Add typed stage context to `PipelineExecutionContext`, such as:
  - issue number
  - PR number
  - PR URL
  - head branch
  - base branch
  - target repository
  - current run ID
  - prior stage summaries
  - cached PR diff metadata
- Update `PromptBuilder` to render a compact, structured context block before the stage prompt.
- Prefer context like "Review PR #123 at URL X" over "Find the PR for issue #123."
- Add context pruning rules by stage:
  - triage: issue title/body/labels only
  - plan: issue + triage summary
  - implement: issue + plan artifact + branch info
  - review: PR number/url/head branch + diff summary + implementation artifact
  - docs: PR diff summary + review verdict
  - deliver: PR URL + approvals + validation evidence

Target files:

- `PipelineExecutionContext.cs`
- `PromptBuilder.cs`
- `PipelineEngine.cs`
- Stage prompt files under `.github/agents`

Success criteria:

- Stage prompts are shorter and deterministic.
- Agents no longer perform avoidable issue/PR lookup steps when the harness already has that state.
- Review/docs/deliver stages receive PR-first context.

### Phase 3: Custom tools for deterministic operations

Goal: replace expensive model-driven CLI/API discovery with typed, cheap, auditable tools.

Candidate tools:

- `get_pipeline_context`: returns issue, PR, branch, repo, current stage, prior artifacts.
- `get_pr_details`: returns PR metadata, changed files, head/base branch, status.
- `get_pr_diff_summary`: returns cached diff metadata and optionally full diff chunks.
- `post_stage_comment`: writes a normalized issue/PR comment.
- `record_stage_artifact`: persists structured artifacts to the app state store.
- `run_validation_command`: executes whitelisted validation commands with normalized output.
- `set_pipeline_label`: manages SDK labels consistently.

Design notes:

- Use the SDK's custom tool support rather than asking the model to call arbitrary shell/GitHub commands for known operations.
- Keep tools narrow, typed, and whitelisted.
- Return compact results optimized for model consumption, with detailed output persisted separately for UI display.

Target files:

- New tool definitions under `copilot-sdk/Copilot/Tools`
- `CopilotStageRunner.cs`
- Service interfaces for GitHub, git, validation, and pipeline state

Success criteria:

- Review and docs use a typed PR tool instead of searching issue comments.
- Stage comments and artifacts are normalized.
- Tool output is compact enough to avoid token bloat.

### Phase 4: Hook-based guardrails and output shaping

Goal: use SDK hooks to make tool use safer and less noisy.

Work:

- Add `onPreToolUse` hook:
  - allow/deny tools based on stage
  - prevent writes during read-only review dimensions
  - normalize tool arguments
  - add default timeouts
  - suppress known-noisy tool output when the model does not need full content
- Add `onPostToolUse` hook:
  - redact secrets from tool results
  - truncate oversized outputs
  - summarize repetitive directory/search results
  - store detailed output separately for the dashboard
  - add debugging hints on failed commands
- Add stage-specific tool policies:
  - triage/plan: read + GitHub comments
  - implement: read/write/shell/git/GitHub PR creation
  - review: read/GitHub PR review/validation, no broad write operations
  - docs: docs-focused writes only
  - deliver: merge/branch cleanup/comment only

Target files:

- `CopilotStageRunner.cs`
- New `StageToolPolicy` model
- Pipeline definitions or stage configuration

Success criteria:

- Tool output tokens drop without losing UI detail.
- Risky tool calls are blocked before execution.
- Secret-looking output is redacted before it reaches the model conversation.

### Phase 5: Tiered model selection and fallback

Goal: match model cost and capability to stage difficulty.

Work:

- Add per-stage model configuration:
  - default/global model remains available
  - `StageModels` maps stage name to model
  - optional override from UI/run request
- Suggested defaults:
  - triage: fast/cheap model
  - plan: standard model
  - implement: strong coding model
  - review: strong reasoning/review model
  - docs: fast/cheap or standard model
  - deliver: fast/cheap model
- Add model fallback chain:
  - check availability before stage start
  - retry with fallback on model-unavailable errors
  - record which model actually ran

Target files:

- `CyberpilotOptions.cs`
- `CyberpilotRunRequest.cs`
- `WebPipelineRunRequest.cs`
- `StageExecutor.cs`
- `CopilotStageRunner.cs`
- Web model selector UI

Success criteria:

- Most cheap stages do not use premium models by default.
- A stage records selected model, fallback model, and fallback reason.
- Model outages degrade gracefully when a configured fallback is available.

### Phase 6: Session persistence, pause, resume, and human steering

Goal: make intervention native instead of treating each stage as disposable.

Work:

- Assign stable SDK session IDs using run/stage identity, e.g. `cyberpilot-{runId}-{stageName}-{attempt}`.
- Persist SDK session ID on pipeline runs/stages.
- Use session resume for paused or interrupted stages where safe.
- Add UI support for:
  - "pause after current turn"
  - "resume stage"
  - "steer current stage" using immediate mode
  - "queue follow-up instruction" using enqueue mode
- Carefully define when resume is allowed:
  - safe for analysis/review
  - risky after partial writes unless state has been reconciled

Target files:

- `CopilotStageRunner.cs`
- Run persistence models
- `PipelinesController.RunLifecycle.cs`
- Dashboard views

Success criteria:

- Human intervention can alter an active stage without killing the run.
- Restarted web app can resume or cleanly reconnect to stage state.
- Session ownership and cleanup are explicit.

### Phase 7: Parallelize review safely

Goal: reduce wall-clock time for the slowest pipeline phase.

Options:

1. **SDK custom agents inside one session**
   - Define security, quality, architecture, test, and docs reviewers as custom agents.
   - Let the review orchestrator delegate specialized checks.
   - Use `parentToolCallId` and sub-agent event data for metrics.

2. **Harness-level parallel sessions**
   - Start separate read-only review sessions per dimension.
   - Merge findings deterministically.
   - Run final verdict session only after dimension outputs are collected.

Preferred starting point:

- Prototype harness-level parallel sessions first. It gives clearer isolation, metrics, timeout control, and failure handling.

Target files:

- `PipelineEngine.cs`
- `CopilotStageRunner.cs`
- `.github/agents/pipeline-review.agent.md`
- New review dimension models

Success criteria:

- Security/quality/architecture/test/docs checks run concurrently.
- Final verdict remains deterministic and policy-driven.
- One failed dimension does not hide the others.

### Phase 8: MCP and repository intelligence

Goal: make repo/GitHub/database access more structured and portable.

Work:

- Evaluate MCP servers for:
  - GitHub API access
  - filesystem access with restricted roots
  - SQLite/app database access
  - browser/UI validation
- Prefer custom tools for tightly controlled Cyberpilot operations.
- Prefer MCP when the tool is general-purpose, mature, and worth reusing.
- Add MCP configuration per session/stage only when needed.

Target files:

- `CopilotStageRunner.cs`
- appsettings repo configuration
- optional MCP server config models

Success criteria:

- Agents get fewer broad shell permissions.
- Repository and state access are typed and permission-scoped.
- MCP failures are observable and recoverable.

## Suggested execution order

1. Phase 0: SDK reference and decision log
2. Phase 1: Observability-first instrumentation
3. Phase 2: Harness-owned state and context injection
4. Phase 3: Custom tools for deterministic operations
5. Phase 4: Hook-based guardrails and output shaping
6. Phase 5: Tiered model selection and fallback
7. Phase 6: Session persistence, pause, resume, and human steering
8. Phase 7: Parallelize review safely
9. Phase 8: MCP and repository intelligence

## Open design questions

- Should Cyberpilot use one SDK session per stage, one per pipeline run, or a hybrid?
- Should review parallelization happen inside a single SDK session using custom agents, or at the harness level using multiple sessions?
- Which stage outputs should become first-class database artifacts instead of issue comments?
- What is the safe default tool policy per stage?
- Should external PR review runs use a different pipeline definition than issue-originated runs?
- How much raw tool output should be persisted for humans but withheld from model context?
- What model tiers should be the default per stage?
- What is the cleanup policy for persisted SDK sessions and run artifacts?

## Immediate next review checklist

- Confirm the roadmap order.
- Decide whether Phase 1 metrics should be database-only, UI-visible, or both.
- Decide whether Phase 3 custom tools should live in the SDK project or in the web host.
- Pick the first concrete optimization slice after review.
