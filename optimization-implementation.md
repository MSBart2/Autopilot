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

### Investigation findings (2026-05-18)

Stage-level failure rates across all 4 clean baseline runs (by `ToolCallCount` aggregate):

| Stage | Failed / Total | Rate |
| --- | --- | --- |
| plan | 40 / 88 | 45.5% |
| triage | 19 / 44 | 43.2% |
| review | 37 / 108 | 34.3% |
| implement | 44 / 133 | 33.1% |
| docs | 25 / 82 | 30.5% |

**Root cause visibility is limited.** `ToolExecutionCompleteData` carries an `Error.Code` and `Error.Message` when `Success = false`, but Cyberpilot currently only increments `FailedToolCallCount` — it does not log the failure reason. The `PipelineArtifacts` table (from post-tool-use hooks) only records tool calls that executed; denied calls and SDK-level failures produce no artifact, so they are invisible.

**Confirmed failure types from artifact analysis** (partial — post-hook artifacts only, 488 total):

| Type | Count | Example |
| --- | --- | --- |
| PowerShell multi-arg error | 7 | `accepts 1 arg(s), received 6 — exited with exit code 1` |
| GitHub self-review block | 4 | `Review: Can not approve/request changes on your own pull request` |
| GitHub GraphQL TLS timeout | 3 | `Post https://api.github.com/graphql: net/http: TLS handshake timeout` |

Remaining ~151 failures are not captured — most likely pre-hook write denials in read-only stages (plan, triage, review) and SDK-level failures that produce no artifact.

### Investigation tasks

- [x] Query `PipelineStageLogs` to identify which stages have the highest `FailedToolCallCount / ToolCallCount` ratio.
- [ ] Add failure reason logging — capture `ToolExecutionCompleteData.Error.Code` and `Error.Message` when `Success = false` to make root causes inspectable.
- [ ] Determine what fraction of failures are pre-hook write denials in read-only stages vs. tool execution errors.
- [ ] Confirm whether the self-review error is from a stage prompt instructing the agent to approve PRs, and fix the prompt or add a pre-hook denial.
- [ ] Investigate the PowerShell multi-arg errors — determine which commands trigger them and whether tightening the tool description prevents them.
- [ ] Determine whether failures are retried by the model (wasted turns) or silently skipped.

### Candidate fixes

| Root cause | Candidate fix | Status |
| --- | --- | --- |
| Failure reasons not logged | Log `ToolExecutionCompleteData.Error` (code + message) per failed call | Not started |
| Review agent tries to approve its own PR | Remove or gate the PR approval step in the review stage prompt | Not started |
| Agent passes multi-word commands as separate PowerShell args | Improve PowerShell tool description to require a single command string | Not started |
| Model retries a failing tool in a loop | Add post-tool hook to detect repeat failures and surface a hint | Not started |
| Tool times out on slow operations | Add per-tool timeout config with a sensible default | Not started |
| Read-only stage agent attempts file writes | Confirm pre-hook denial messages reach the model; add denial hint in prompt | Not started |
| GitHub API TLS timeouts | Add retry with backoff for transient network failures | Not started |

### Success criteria

- [ ] `FailedToolCallCount / ToolCallCount` drops below 20% on a full benchmark run.
- [ ] No regression in stage output quality or valid JSON rate.
- [ ] Per-stage failure counts recorded in `metrics.md` before and after each fix.
- [ ] Failed tool calls include a logged reason code — no more invisible failures.

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
| 2026-05-18 | Start with tool failure rate reduction before prompt/context work | Baseline shows 32–46% failure rate — highest-leverage fix with no architecture risk | Failure reason logging implemented so all root causes are visible |
| TBD | Start with PR-first review for prompt/context A/B tests | It isolates review routing and avoids full pipeline noise | Full issue baselines are available |

## Open questions

- Which exact workflows should be the first deterministic promotion candidates after PR-first context?
- Should prompt/system-message A/B be stored per run, per stage, or only in logs?
- What minimum sample size is enough before enabling an optimization by default?
- Should baseline scenarios live as actual seeded test issues/PRs in a sandbox repo?
- How should we normalize quality outcomes so token reductions do not hide worse review decisions?
