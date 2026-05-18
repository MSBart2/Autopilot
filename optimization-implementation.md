# Cyberpilot Optimization Implementation Tracker

This tracker turns `optimization-plan.md` into measurable implementation work. Measurement procedures, SQL queries, benchmark scenarios, and the data log live in [`metrics.md`](metrics.md).

## Milestone 0: SDK reference and decision log

- [ ] Create `docs/copilot-sdk-references.md` with SDK feature links and Cyberpilot-specific notes.
- [ ] Update `AGENTS.md` to point SDK/session orchestration work at `docs/copilot-sdk-references.md`.
- [ ] Add a short SDK harness decision log covering session lifetime, streaming metrics, prompt hierarchy, tool policy, and hook/tool placement.
- [ ] Record any SDK preview caveats that could affect implementation.

### Validation / baseline notes

- No runtime baseline required.
- Confirm future agents have one canonical reference before changing SDK integration code.

## Milestone 1: Observability baseline

- [ ] Capture current prompt character count per stage.
- [ ] Capture current input/output/cache token metrics per stage.
- [ ] Capture current `assistant.turn_start` / `assistant.turn_end` counts per stage.
- [ ] Capture tool execution counts and failed tool counts per stage.
- [ ] Capture stage duration and total run duration.
- [ ] Persist enough metrics to compare experiments across runs.
- [ ] Surface the minimum useful metrics in the Run Room or logs.

### Baseline exit criteria

- [ ] One `baseline-aspire-docs` run recorded.
- [ ] One `baseline-aspire-helper` run recorded.
- [ ] One `baseline-aspire-ui` run recorded.
- [ ] One `baseline-pr-review` run recorded.
- [ ] Metrics are recorded at stage granularity.
- [ ] We can identify the most expensive stage by tokens, turns, and time.

## Milestone 1.5: Tool failure rate reduction

Baseline runs show ~34–38% of tool calls failing across all stages (44/131 on #33 run 1, 43/133 on #32 run 2). This is the highest-leverage near-term target — reducing failures drives down turns, tokens, duration, and cost without requiring prompt or architecture changes.

### Investigation tasks

- [ ] Query `PipelineStageLogs` to identify which stages have the highest `FailedToolCallCount / ToolCallCount` ratio.
- [ ] Correlate failed tool calls with stage output logs to identify the most common failure modes (bad args, missing files, permission errors, tool not found, timeout, etc.).
- [ ] Determine whether failures are retried by the model (wasted turns) or silently skipped.
- [ ] Identify whether any failures are expected/benign (e.g. probing for a file that may not exist) vs. avoidable errors.

### Candidate fixes

| Root cause | Candidate fix | Status |
| --- | --- | --- |
| Model passes bad args to known tools | Improve tool descriptions / arg validation error messages | Not started |
| Model retries a failing tool in a loop | Add post-tool hook to detect repeat failures and surface a hint | Not started |
| Tool times out on slow operations | Add per-tool timeout config with a sensible default | Not started |
| Tool not available in current stage policy | Tighten stage tool policy so unavailable tools are never offered | Not started |
| Model probes for files that don't exist | Structured context pre-populates known file paths | Not started |

### Success criteria

- [ ] `FailedToolCallCount / ToolCallCount` drops below 20% on a full benchmark run.
- [ ] No regression in stage output quality or valid JSON rate.
- [ ] Per-stage failure counts recorded in `metrics.md` before and after each fix.

### Baseline failure rates (for comparison)

| Run | Scenario | Failed / Total | Rate |
| --- | --- | --- | --- |
| `9e82c517` | #33 run 1 | 44 / 131 | 34% |
| `50b6b021` | #33 run 2 | 32 / 70 | 46% |
| `724eb0c3` | #34 run 1 | 46 / 121 | 38% |
| `edd0d6ea` | #32 run 2 | 43 / 133 | 32% |



- [ ] Draft compact Cyberpilot harness system prompt.
- [ ] Split prompt responsibilities:
  - system prompt = harness law
  - structured context = runtime facts
  - stage prompt = role and judgment criteria
  - stage mission = current task
- [ ] Wire system prompt behind config or feature flag; do not enable by default.
- [ ] Verify whether SDK `SystemMessage` augments or replaces default Copilot runtime behavior.
- [ ] Run A/B comparison on `baseline-pr-review`.
- [ ] Record valid JSON rate, turns, tool calls, token usage, prompt size, and runtime.

### Success criteria

- [ ] Stage prompts no longer need repeated controller boilerplate.
- [ ] Review no longer performs known PR/issue rediscovery when structured context contains the answer.
- [ ] JSON validity and artifact validation do not regress.
- [ ] Turns/tool calls/token usage improve or stay flat with clearer behavior.

## Milestone 3: Harness-owned structured context

- [ ] Define a typed stage context model.
- [ ] Populate issue number, PR number, PR URL, branch, base branch, repository, run ID, prior artifacts, and known approvals.
- [ ] Add stage-specific context pruning rules.
- [ ] Render structured context in a compact machine-readable block.
- [ ] Remove prompt instructions that ask agents to rediscover known routing state.
- [ ] Run before/after comparison on full issue and PR-first review scenarios.

### Success criteria

- [ ] Review/docs/deliver receive PR-first context when known.
- [ ] Prompt size decreases or stays flat while useful context improves.
- [ ] Discovery-related tool calls decrease.

## Milestone 4: Deterministic workflow promotion

- [ ] Identify repeated model-discovered workflows from stage logs.
- [ ] Rank candidates by frequency, token cost, runtime cost, and implementation risk.
- [ ] Promote the first deterministic workflow into code/tool/gate/script.
- [ ] Add tests for promoted deterministic behavior.
- [ ] Record before/after metrics.

### Candidate workflows

| Workflow | Likely owner | Candidate implementation | Status |
| --- | --- | --- | --- |
| Find PR for known PR-first run | Harness | Structured context / route shortcut | Not started |
| Compute next stage route | Harness | `compute_stage_route` gate/helper | Not started |
| Fetch PR metadata | Tool | `get_pr_details` | Not started |
| Fetch/summarize PR diff | Tool/cache | `get_pr_diff_summary` | Not started |
| Run known validation commands | Script/tool | `collect_validation_evidence` | Not started |
| Render stage comments | Harness/tool | `render_stage_comment` | Not started |
| Persist stage artifacts | Harness/tool | `record_stage_artifact` | Not started |
| Manage SDK labels | Harness/tool | `set_pipeline_label` | Not started |

### Success criteria

- [ ] At least one repeated workflow no longer requires model-invented shell/API steps.
- [ ] The promoted workflow emits typed results.
- [ ] Tool/gate output is compact for the model and detailed enough for humans.

## Milestone 5: Hook-based output shaping and safety

- [ ] Add stage tool policy model.
- [ ] Implement pre-tool guardrails for write restrictions, default timeouts, and allowed tools.
- [ ] Implement post-tool redaction for secret-looking output.
- [ ] Implement post-tool truncation/summarization for noisy output.
- [ ] Persist raw/detailed output separately from compact model context where useful.
- [ ] Compare token usage before/after noisy output shaping.

### Success criteria

- [ ] Read-only stages cannot perform broad writes.
- [ ] Secret-looking output is redacted before model context.
- [ ] Token usage from tool output decreases or stays flat without reducing human auditability.

## Milestone 6: Per-stage model tiers and fallback

- [ ] Add stage-specific model config.
- [ ] Add request/UI override behavior.
- [ ] Add fallback model chain.
- [ ] Record selected model, fallback model, and fallback reason per stage.
- [ ] Compare cost/time/quality on cheap stages and review/implement stages.

### Success criteria

- [ ] Cheap stages can use cheaper defaults without global model changes.
- [ ] Model unavailability can fall back gracefully.
- [ ] Actual model usage is visible in metrics.

## Milestone 7: Session persistence and human steering

- [ ] Persist stable SDK session IDs by run/stage/attempt.
- [ ] Define resume eligibility rules.
- [ ] Add pause-after-turn concept.
- [ ] Add immediate steering concept.
- [ ] Add queued follow-up concept.
- [ ] Add cleanup rules for abandoned or completed sessions.

### Success criteria

- [ ] Operators can intervene without killing a run.
- [ ] Unsafe resume cases fail closed with clear required actions.
- [ ] Session cleanup is explicit.

## Milestone 8: Parallel review dimensions

- [ ] Prototype harness-level read-only sessions for security, quality, architecture, tests, and docs review dimensions.
- [ ] Merge dimension outputs into deterministic final verdict input.
- [ ] Capture dimension-specific metrics.
- [ ] Compare wall-clock time and total token cost against current review.

### Success criteria

- [ ] Review dimensions run concurrently.
- [ ] One failed dimension does not hide other results.
- [ ] Final verdict remains deterministic and policy-driven.

## Milestone 9: MCP evaluation

- [ ] Evaluate GitHub MCP server against custom GitHub tools.
- [ ] Evaluate filesystem MCP against existing repo tools and stage policies.
- [ ] Evaluate SQLite/database access options.
- [ ] Document which integrations should stay custom tools vs MCP.

### Success criteria

- [ ] Any enabled MCP server is stage-scoped, permission-scoped, observable, and recoverable.
- [ ] MCP is used only where it beats custom tools for control, maturity, and maintenance cost.

## Decision log

| Date | Decision | Reason | Revisit when |
| --- | --- | --- | --- |
| 2026-05-18 | Use stage-level metrics as primary optimization unit | Stage totals reveal bottlenecks better than run totals | Metrics model changes |
| 2026-05-18 | Start with tool failure rate reduction before prompt/context work | Baseline shows 32–46% failure rate — highest-leverage fix with no architecture risk | Failure root causes identified |
| TBD | Start with PR-first review for prompt/context A/B tests | It isolates review routing and avoids full pipeline noise | Full issue baselines are available |

## Open questions

- Which exact workflows should be the first deterministic promotion candidates after PR-first context?
- Should prompt/system-message A/B be stored per run, per stage, or only in logs?
- What minimum sample size is enough before enabling an optimization by default?
- Should baseline scenarios live as actual seeded test issues/PRs in a sandbox repo?
- How should we normalize quality outcomes so token reductions do not hide worse review decisions?
