# Cyberpilot Optimization Plan

## Purpose

Cyberpilot is becoming a custom harness around GitHub Copilot SDK sessions, not just a wrapper that fires one prompt per pipeline stage. The next optimization pass should make that harness cheaper, faster, more observable, easier to steer, and less dependent on model-discovered workflow state.

The guiding theme is to reduce the amount of "polite blending" the model has to do. Cyberpilot should own deterministic workflow mechanics in code, scripts, tools, gates, and typed state. The model should focus on judgment-heavy work: interpretation, tradeoffs, implementation reasoning, review findings, summaries, and decisions that genuinely require language/code understanding.

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
7. **Do not ask the model to invent deterministic mechanics.** If an action can be expressed as code, configuration, a script, a query, or a typed API call, make the harness do it and provide the result to the model.

## Harness vs. model responsibility split

Cyberpilot should explicitly separate harness law, deterministic mechanics, runtime facts, and model judgment.

| Responsibility | Owner | Examples | Why |
| --- | --- | --- | --- |
| Harness law | System prompt + code | Stage contract rules, JSON result requirements, label ownership, tool policy, safety boundaries | These are durable invariants and should not compete with stage prompt prose. |
| Runtime facts | Harness state/context | issue number, PR number, PR URL, branch, base, repository, run ID, prior artifacts, known approvals | The harness already knows these; the model should not spend turns rediscovering them. |
| Deterministic mechanics | Code/scripts/custom tools/gates | route stage, find linked PR, fetch diff, set labels, post normalized comments, run validation commands, compute status, persist artifacts | These are cheaper, auditable, testable, and more reliable outside the model. |
| Policy evaluation | Code + model, depending on ambiguity | required labels, PR presence, approval state, validation pass/fail, severity threshold enforcement | Objective checks should be code; ambiguous review interpretation can remain model-assisted. |
| Judgment-heavy work | Model | implementation strategy, code changes, architectural review, security reasoning, docs wording, final review synthesis | This is where language/code understanding and tradeoff reasoning are valuable. |

Default rule:

- If the next step has a known API, deterministic algorithm, stable script, database query, or repository command, implement it as harness behavior or a custom tool.
- If the next step requires weighing tradeoffs, interpreting code intent, writing code, reviewing nuanced risk, or explaining decisions, give it to the model with typed context and a narrow mission.
- If the model repeatedly performs the same discovery or shell sequence, promote that sequence into a tool, gate, cache, or script.

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

### Phase 2A: Cyberpilot harness system prompt and prompt split

Goal: move durable controller rules out of per-stage prose and into a compact, stable harness-level system prompt.

Work:

- Define a Cyberpilot harness system prompt containing only durable invariants:
  - Cyberpilot is SDK-controlled and stage-scoped.
  - Harness state and typed tools are authoritative.
  - Use provided structured context before discovery.
  - Do not manage `sdk` labels directly.
  - Do not close issues unless the deliver contract explicitly permits it.
  - Respect stage tool policy.
  - Return valid stage-result JSON matching the contract.
- Keep stage agent prompts focused on role, expertise, review/implementation criteria, and voice.
- Keep user/stage messages focused on:
  - stage name
  - mission
  - typed context
  - required artifacts
  - stage-specific policy profile
- Validate SDK behavior before committing to this architecture:
  - determine whether `SystemMessage` augments or replaces the default Copilot runtime prompt
  - verify tools, permissions, custom agents, and JSON result parsing still behave correctly
  - compare valid JSON rate, turn count, tool count, token usage, and retries before/after

Target files:

- `CopilotStageRunner.cs`
- `PromptBuilder.cs`
- New `CyberpilotSystemPrompt` or prompt template file
- Stage prompt files under `.github/agents`

Success criteria:

- Durable controller rules are defined once instead of repeated and blended into every stage mission.
- Stage prompts get shorter and more role-specific.
- The model receives a clear hierarchy: system prompt = harness law, structured context = facts, stage mission = current task.
- SDK compatibility is proven before replacing or overriding any default system behavior.

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
- `compute_stage_route`: determines the next stage from definition, gates, prior result, and run state.
- `collect_validation_evidence`: runs known build/test/lint commands from repository profile and returns normalized evidence.
- `render_stage_comment`: converts structured artifacts into consistent issue/PR comments.
- `prepare_review_inputs`: builds PR-first review context from PR metadata, changed files, diff summary, prior implementation artifact, and policy profile.

Design notes:

- Use the SDK's custom tool support rather than asking the model to call arbitrary shell/GitHub commands for known operations.
- Keep tools narrow, typed, and whitelisted.
- Return compact results optimized for model consumption, with detailed output persisted separately for UI display.
- Prefer custom tools or harness code for repeatable workflows that currently require the model to "figure out what to do next."
- The model may decide *whether* a validation result is sufficient, but the harness should decide *how* to run and normalize the validation command.

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
4. Phase 2A: Cyberpilot harness system prompt and prompt split
5. Phase 3: Custom tools for deterministic operations
6. Phase 4: Hook-based guardrails and output shaping
7. Phase 5: Tiered model selection and fallback
8. Phase 6: Session persistence, pause, resume, and human steering
9. Phase 7: Parallelize review safely
10. Phase 8: MCP and repository intelligence

## Open design questions

- Should Cyberpilot use one SDK session per stage, one per pipeline run, or a hybrid?
- Should review parallelization happen inside a single SDK session using custom agents, or at the harness level using multiple sessions?
- Which stage outputs should become first-class database artifacts instead of issue comments?
- What is the safe default tool policy per stage?
- Should external PR review runs use a different pipeline definition than issue-originated runs?
- How much raw tool output should be persisted for humans but withheld from model context?
- What model tiers should be the default per stage?
- What is the cleanup policy for persisted SDK sessions and run artifacts?

## Proposed decisions

These decisions are the recommended starting point for implementation. Revisit them as SDK behavior, cost data, or operator feedback changes.

| Question | Proposed decision | Rationale |
| --- | --- | --- |
| Session lifetime | Use a hybrid model with one SDK session per stage as the default. Use stable run/stage/attempt session IDs so later phases can resume or clean up sessions explicitly. | Stage isolation already matches the current harness and keeps retries, labels, logs, and tool policy boundaries clear. Stable IDs create the path to resume without merging every stage into one long-lived conversation. |
| Review parallelization | Prototype harness-level parallel review sessions first. Keep a final review verdict session after dimension outputs are collected. | Separate sessions give clearer isolation, timeout control, read-only permissions, metrics, and partial-failure handling than custom agents inside one conversation. |
| First-class artifacts | Promote plan summaries, implementation summaries, PR metadata, diff summaries, validation results, review findings/verdicts, docs verification, delivery evidence, and approval decisions into the database. Keep issue/PR comments as human-readable reports. | The harness should own workflow state instead of asking later stages to rediscover it from comments. Comments remain useful communication, but they should not be the canonical state store. |
| Stage tool policy | Default to least privilege by stage: triage/plan get read plus GitHub comments, implement gets repo write/shell/git/PR creation, review gets read/validation/PR review with no broad writes, docs gets docs-focused writes, and deliver gets merge/comment/cleanup only. | Tool policy should match stage responsibility and prevent accidental writes during analysis or review. |
| External PR review flow | Add a PR-first pipeline definition for external review runs instead of forcing them through issue-originated routing. | External PR review starts from PR number, branch, base, and diff metadata. It should not spend model turns on issue triage or branch creation. |
| Raw tool output | Persist redacted raw output for humans with size limits and retention rules. Feed compact summaries to the model by default, with raw detail opt-in only when a stage needs it. | Humans need auditability and troubleshooting detail; the model usually needs a concise signal. This reduces token bloat without hiding operational evidence. |
| Model tiers | Use cheap/fast models for triage, docs, and deliver; standard models for plan; strong coding models for implement; and strong reasoning/review models for review. Record selected model, fallback model, and fallback reason per stage. | Most stages do not need premium model capacity. Recording the actual model makes cost and quality comparisons possible. |
| Cleanup | Keep structured artifacts and high-level metrics with run history. Retain raw tool output and resumable SDK session records for a shorter configurable window. Reset Mission should delete local session/artifact state for the run unless delivery already completed. | Long-lived history should stay useful and compact, while bulky transcripts and resumable session state need explicit lifecycle management. |
| Prompt hierarchy | Introduce a compact Cyberpilot harness system prompt for durable controller rules, keep stage prompts role-focused, and pass runtime facts as structured context. Validate whether SDK `SystemMessage` augments or replaces default runtime behavior before relying on it. | This reduces instruction blending and gives the model a clearer separation between harness law, facts, and current-stage judgment. |
| Code/script/tool promotion | Promote repeated deterministic model behavior into harness code, custom tools, gates, or whitelisted scripts. Leave only ambiguous judgment and synthesis to the model. | Deterministic operations are cheaper, testable, auditable, and less likely to drift than model-invented shell/API sequences. |

## Immediate next review checklist

- Confirm the roadmap order.
- Decide whether Phase 1 metrics should be database-only, UI-visible, or both.
- Decide whether Phase 3 custom tools should live in the SDK project or in the web host.
- Pick the first concrete optimization slice after review.

## Issue-ready chunks

Use these chunks as the initial GitHub issue backlog. Each issue should include acceptance criteria, validation notes, and any affected docs.

### Chunk 1: Document SDK harness decisions

Phase: 0

Status: Complete

Scope:

- Create `docs/copilot-sdk-references.md` with SDK links, supported features, and Cyberpilot integration notes.
- Update `AGENTS.md` to point SDK-related work to the reference document.
- Add the initial SDK harness decision log covering stage-scoped sessions, streaming metrics, tool policy, and future hook/tool placement.

Acceptance criteria:

- Future agents have one repo-local reference before changing SDK integration code.
- The decision log states the current default session lifetime and where to record future SDK design changes.

### Chunk 2: Capture rich stage metrics

Phase: 1

Status: Complete

Scope:

- Subscribe to streaming events for assistant turns, usage, tool execution, session errors, and idle/completion.
- Add or extend a `StageExecutionMetrics` model with model, tokens, cache tokens, duration, turn count, tool counts, failed tool counts, and provider request identifiers where available.
- Keep existing final usage capture as a fallback when streaming usage events are unavailable.

Acceptance criteria:

- Each stage result includes richer metrics than final input/output token counts.
- Metrics capture failures are non-fatal and visible in logs.
- Unit tests cover metrics aggregation from representative event sequences where practical.

### Chunk 3: Persist and display stage metrics

Phase: 1

Status: Complete

Scope:

- Persist rich metrics to stage logs or related metric rows.
- Update progress sinks to write selected model, turn count, tool counts, failed tool counts, duration, and cost inputs.
- Surface the metrics in the dashboard so expensive or looping stages are visible.

Acceptance criteria:

- Run details show token usage, turn count, tool count, duration, and error indicators per stage.
- Existing run history remains readable after migration.
- Cost estimates continue to work for known models and degrade to zero/unknown for unmapped models.

### Chunk 4: Inject harness-owned stage context

Phase: 2

Status: Complete

Scope:

- Expand `PipelineExecutionContext` with issue, PR, branch, repository, run, prior-stage, and cached diff metadata.
- Update `PromptBuilder` to render a compact structured context block before the imported stage prompt.
- Add stage-specific pruning rules so each stage receives only useful context.

Acceptance criteria:

- Review, docs, and deliver prompts receive PR-first context when the harness already knows it.
- Stage prompts no longer instruct agents to rediscover known issue/PR routing information.
- Prompt size is reduced or held steady while context quality improves.

### Chunk 5: Promote first-class stage artifacts

Phase: 2

Status: Complete

Scope:

- Define database-backed artifact records for plan, implementation, PR metadata, diff summary, validation, review verdict, docs verification, delivery evidence, and approvals.
- Add artifact write paths from stage results and deterministic gates.
- Keep issue/PR comments as reports generated from stored artifacts where feasible.

Acceptance criteria:

- Later stages can read canonical artifacts without scraping issue comments.
- Run details can show structured artifacts independent of raw stage transcript output.
- Artifact records include stage, run, contract version, summary, optional URI, and timestamps.

### Chunk 6: Add deterministic PR context tools

Phase: 3

Status: Complete

Scope:

- Add custom tools under the SDK project for `get_pipeline_context`, `get_pr_details`, and `get_pr_diff_summary`.
- Return compact typed results optimized for model consumption.
- Persist detailed tool output separately when the UI needs it.

Acceptance criteria:

- Review and docs stages can consume typed PR data instead of searching issue comments.
- Tool outputs are small enough for prompt context and include references to detailed persisted output when available.
- Tool failures return actionable, structured errors.

### Chunk 6A: Add Cyberpilot harness system prompt

Phase: 2A

Scope:

- Define a compact harness-level system prompt for durable Cyberpilot controller invariants.
- Split prompt responsibilities so system prompt covers harness law, structured context covers facts, and stage prompts cover stage-specific expertise.
- Validate whether SDK system-message configuration augments or replaces default Copilot runtime instructions.
- Compare before/after metrics for valid JSON rate, turn count, tool count, token usage, retries, and prompt size.

Acceptance criteria:

- Stage prompts no longer repeat durable controller boilerplate.
- The harness prompt explicitly tells agents to use typed context and tools before rediscovering state.
- SDK compatibility is documented before the new prompt architecture becomes the default.

### Chunk 6B: Promote repeated deterministic workflows into code/tools

Phase: 3

Scope:

- Identify repeated model-discovered workflows in recent stage logs, especially PR lookup, route selection, validation command selection, diff gathering, label management, artifact persistence, and comment rendering.
- Implement the highest-volume workflows as harness code, custom tools, gates, or whitelisted scripts.
- Add a promotion checklist so future repeated prompt/tool sequences are candidates for deterministic implementation.

Acceptance criteria:

- The model no longer has to invent command/API sequences for the selected workflows.
- Deterministic workflows are unit-tested where practical and emit structured results.
- The plan documents which workflows remain model-owned because they require judgment.

### Chunk 7: Add stage tool policies and hooks

Phase: 4

Status: Complete

Scope:

- Add a stage tool policy model and wire pre-tool guardrails into SDK session configuration.
- Deny writes by default outside stages that require them.
- Add post-tool redaction, truncation, and noisy-output shaping.

Acceptance criteria:

- Read-only stages cannot perform broad write operations.
- Secret-looking output is redacted before it reaches the model context.
- Raw and summarized tool outputs are both auditable from run history where configured.

### Chunk 8: Add per-stage model tiers and fallback

Phase: 5

Status: Complete

Scope:

- Add stage-specific model configuration with CLI, web, and request-level overrides.
- Check model availability before stage start and use configured fallbacks for model-unavailable failures.
- Record selected model, fallback model, and fallback reason per stage.

Acceptance criteria:

- Cheap stages can use cheaper default models without changing the global model.
- Stage logs show the model that actually ran.
- Model outages degrade gracefully when a fallback is configured.

### Chunk 9: Add session persistence and steering primitives

Phase: 6

Scope:

- Persist SDK session IDs using run/stage/attempt identity.
- Add pause-after-turn, resume-stage, immediate steering, and queued follow-up concepts to the run lifecycle.
- Define resume eligibility rules for read-only, write-capable, failed, and interrupted stages.

Acceptance criteria:

- Operators can inspect and steer an active stage without killing the run.
- Restarted web runs can reconnect, resume, or cleanly mark session state as abandoned.
- Unsafe resume cases fail closed with clear required actions.

### Chunk 10: Parallelize review dimensions

Phase: 7

Scope:

- Add harness-level read-only review dimension sessions for security, quality, architecture, tests, and docs.
- Merge dimension findings into a deterministic final verdict session.
- Capture dimension-specific metrics and failures.

Acceptance criteria:

- Review dimensions run concurrently under read-only tool policy.
- One failed dimension does not hide other dimension findings.
- Final verdict is deterministic and based on collected dimension outputs plus policy profile.

### Chunk 11: Evaluate MCP integration points

Phase: 8

Scope:

- Evaluate MCP servers for GitHub, filesystem, SQLite/app database access, and browser validation.
- Compare MCP against Cyberpilot custom tools for control, maturity, observability, and failure handling.
- Add configuration only for the integrations that beat custom tools for the use case.

Acceptance criteria:

- The plan documents which integrations should remain custom tools and which should use MCP.
- Any enabled MCP server is stage-scoped, permission-scoped, observable, and recoverable.
