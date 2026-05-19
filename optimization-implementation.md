# Cyberpilot Optimization Implementation Tracker

This tracker turns `optimization-plan.md` into measurable implementation work. Measurement procedures, SQL queries, benchmark scenarios, and the data log live in [`metrics.md`](metrics.md).

## Milestone 0: SDK reference and decision log

- [x] Create `docs/copilot-sdk-references.md` with SDK feature links and Cyberpilot-specific notes.
- [x] Update `AGENTS.md` to point SDK/session orchestration work at `docs/copilot-sdk-references.md`.
- [x] Add a short SDK harness decision log covering session lifetime, streaming metrics, prompt hierarchy, tool policy, and hook/tool placement.
- [x] Record any SDK preview caveats that could affect implementation.

### Validation / baseline notes

- No runtime baseline required.
- Confirm future agents have one canonical reference before changing SDK integration code.

## Milestone 1: Observability baseline

- [x] Capture current prompt character count per stage.
- [x] Capture current input/output/cache token metrics per stage.
- [x] Capture current `assistant.turn_start` / `assistant.turn_end` counts per stage.
- [x] Capture tool execution counts and failed tool counts per stage.
- [x] Capture stage duration and total run duration.
- [x] Persist enough metrics to compare experiments across runs.
- [x] Surface the minimum useful metrics in the Run Room or logs.

### Baseline exit criteria

- [x] One `baseline-aspire-docs` run recorded.
- [x] One `baseline-aspire-helper` run recorded. (issues #32, #34)
- [x] One `baseline-aspire-ui` run recorded.
- [x] One `baseline-pr-review` run recorded.
- [x] Metrics are recorded at stage granularity.
- [x] We can identify the most expensive stage by tokens, turns, and time.

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
- [x] Add failure reason logging — capture `ToolExecutionCompleteData.Error.Code` and `Error.Message` when `Success = false` to make root causes inspectable.
- [x] Determine what fraction of failures are pre-hook write denials in read-only stages vs. tool execution errors.
- [x] Confirm whether the self-review error is from a stage prompt instructing the agent to approve PRs, and fix the prompt or add a pre-hook denial.
- [x] Investigate the PowerShell multi-arg errors — determine which commands trigger them and whether tightening the tool description prevents them.
- [x] Determine whether failures are retried by the model (wasted turns) or silently skipped.

### Implementation steps

#### Step 1: Failure reason logging

**Goal:** Make every failed tool call inspectable — error code, error message, tool name.

**Why first:** 90%+ of failures have no logged reason today. Without this, all other fix attempts are guesswork. Logging failure reasons is a prerequisite for confirming any fix worked.

**What to build:**
- Track `ToolCallId → ToolName` in `StageExecutionMetricsCollector` by saving start event data.
- On `RecordToolExecutionComplete` with `Success = false`, capture `ToolName`, `Error.Code`, and `Error.Message`.
- Add a `PipelineToolCallFailure` EF entity: `RunId`, `StageName`, `ToolCallId`, `ToolName`, `ErrorCode`, `ErrorMessage`, `CreatedAt`.
- Persist failures via `CyberpilotRunHistoryProgressSink` after each stage.
- Add EF migration.

**Done when:** Failed tool calls include a reason code queryable from `cyberpilot.db`. Run a benchmark issue and confirm no invisible failures remain.

✅ **Implemented** (`65bb602`) — `FailedToolCallRecord` added to `StageExecutionMetrics`, `PipelineToolFailures` table created with EF migration, `StageExecutionMetricsCollector` tracks ToolCallId→ToolName and captures error code/message per failure.

**Follow-up** (`b20f5db`) — Added `ToolArgs` capture: `ToolExecutionStartData.Arguments` is serialized and stored per failure so we can see exactly what the tool was called with. Also fixed `SignalRProgressSink` to actually persist `PipelineToolFailures` (it was wired in the SDK sink but not the web sink — `3c69873`). Added `--agent-prompt-root` to the exe CLI so it can run against external repos (`d27a954`).

---

#### Step 2: Fix review self-review block

**Root cause confirmed:** Review stage agent calls `gh pr review --approve` on a PR it authored. GitHub rejects this with `Can not approve your own pull request`.

**Fix:** Add a pre-tool hook denial in `StageToolPolicyHooks.EvaluatePreToolUse` to block any `gh pr review` call that includes `--approve` or `--request-changes`. Return a clear denial reason so the model doesn't retry.

**Done when:** Zero self-review errors in a benchmark review stage.

✅ **Implemented** (`e9b75f4`) — `LooksLikeSelfReviewAttempt` added to `StageToolPolicyHooks`, denies `powershell` calls matching `gh pr review .*(--approve|--request-changes)` with an instructive reason. 3 tests added.

---

#### Step 3: Fix PowerShell multi-arg errors

**Root cause confirmed:** Agent passes shell commands as multiple positional args (e.g. `["git", "status"]`) instead of a single command string. PowerShell responds with `accepts 1 arg(s), received N`.

**Fix:** Improve the `powershell` tool description to explicitly state the command must be a single string. Evaluate whether a pre-tool hook can detect and reject split-arg inputs before they reach PowerShell.

**Done when:** Zero `accepts N arg(s), received N` errors in a benchmark run.

✅ **Implemented** (`4319297`) — `LooksLikePowershellArrayArgs` added to `StageToolPolicyHooks`, detects top-level array or `{ command: [...] }` array and denies with a corrective message. 2 tests added.

---

#### Step 4: Retry hint on repeated tool failures

Add a post-tool hook that injects `AdditionalContext` when the same tool fails twice in a row, suggesting the model try an alternative approach. Requires tracking last-failed tool name in `StageToolPolicyHooks`.

---

#### Step 5: TLS retry backoff for GitHub API

Add 2–3 attempt retry with exponential backoff in `GitHubCli` or `GitHubApiIssueClient` for transient TLS handshake errors on GitHub GraphQL calls.

---

### Candidate fixes

| Root cause | Candidate fix | Status |
| --- | --- | --- |
| Failure reasons not logged | Log `ToolExecutionCompleteData.Error` (code + message) per failed call | ✅ Done (`65bb602`) |
| Review agent tries to approve its own PR | Block `gh pr review --approve/request-changes` in pre-tool hook | ✅ Done (`e9b75f4`) |
| Agent passes multi-word commands as separate PowerShell args | Detect array command arg in pre-hook; deny with corrective message | ✅ Done (`4319297`) |
| Model retries a failing tool in a loop | Post-tool hook: inject retry hint on repeat failure | Deferred |
| GitHub API TLS timeouts | Add retry with backoff for transient network failures | Deferred |
| Tool times out on slow operations | Add per-tool timeout config with a sensible default | Deferred |
| Read-only stage agent attempts durable side effects through scripts | Block mutation patterns across shell/script wrappers; make denial visible; align triage/plan prompts to return artifacts instead of posting comments | ✅ Done |

### Success criteria

- [x] `FailedToolCallCount / ToolCallCount` drops below 20% on a full benchmark run.
- [x] No regression in stage output quality or valid JSON rate.
- [x] Per-stage failure counts recorded in `metrics.md` before and after each fix.
- [x] Failed tool calls include a logged reason code — no more invisible failures.

### Baseline failure rates (for comparison)

| Run | Scenario | Failed / Total | Rate |
| --- | --- | --- | --- |
| `9e82c517` | #33 run 1 | 44 / 131 | 34% |
| `50b6b021` | #33 run 2 | 32 / 70 | 46% |
| `724eb0c3` | #34 run 1 | 46 / 121 | 38% |
| `edd0d6ea` | #32 run 2 | 43 / 133 | 32% |



## Milestone 2: System Prompt Restructuring

Split prompt responsibilities so the SDK `SystemMessage` carries harness law (identity, output format, JSON safety, command guidance) and the user message carries only runtime facts and the stage agent prompt. This eliminates repeated boilerplate from every stage call and enables prompt caching.

- [x] Draft compact Cyberpilot harness system prompt.
- [x] Split prompt responsibilities:
  - system prompt = harness law
  - structured context = runtime facts
  - stage prompt = role and judgment criteria
  - stage mission = current task
- [x] Wire system prompt behind config or feature flag; do not enable by default.
- [x] Verify whether SDK `SystemMessage` augments or replaces default Copilot runtime behavior.
  - **Finding:** `SessionConfig.SystemMessage` accepts a `SystemMessageConfig` with `Mode = SystemMessageMode.Append` (appends to default) or `Replace` (full override). Cyberpilot supports both modes, plus `full` and `lean` harness profiles.
- [x] Run A/B comparison on `baseline-pr-review`.
  - **Finding:** Fresh PR clones of the same #34 fixture commit were required because submitted PR reviews are not safely resettable. `append-lean` won review on runtime, turns, tool calls, failed tool calls, and premium request cost.
- [x] Record valid JSON rate, turns, tool calls, token usage, prompt size, and runtime.
  - **Finding:** Stage-level metrics are recorded in `metrics.md`. Raw prompt character count is not yet persisted per run, so input-token usage is the persisted prompt/context-size proxy.

### Milestone 2 benchmark decisions

| Stage | Winner | Rationale |
| --- | --- | --- |
| triage | `replace-lean` | Fastest one-shot triage run and substantially lower input-token usage than inline-full. |
| plan | `inline-full` | Fastest, lowest input-token use, and fewest failed tools when seeded with the same triage result. |
| review | `append-lean` | Fastest PR-first review run with the fewest turns, tools, failed tools, and lowest premium request cost across fresh PR clones. |

Implementation note: stage-aware defaults now allow global `replace-lean` while overriding `plan` to `inline-full` and `review` to `append-lean`. Explicit CLI `--system-message-mode` / `--system-message-profile` flags still override stage defaults for benchmarking.

### Success criteria

- [x] Stage prompts no longer need repeated controller boilerplate.
- [x] Review no longer performs known PR/issue rediscovery when structured context contains the answer.
- [x] JSON validity and artifact validation do not regress.
- [x] Turns/tool calls/token usage improve or stay flat with clearer behavior.

## Milestone 3: Harness-owned structured context

- [x] Define a typed stage context model.
- [x] Populate issue number, PR number, PR URL, branch, base branch, repository, run ID, prior artifacts, and known approvals.
- [x] Add stage-specific context pruning rules.
- [x] Render structured context in a compact machine-readable block.
- [x] Remove prompt instructions that ask agents to rediscover known routing state.
- [x] Run before/after comparison on full issue and PR-first review scenarios.

### Benchmark results (full pipeline, issue #34)

| | Before (M2 baseline, SHA d1c3d35) | After (M2+M3, SHA 6842057) | Δ |
| --- | ---: | ---: | --- |
| Input tokens | 3,865,250 | 2,048,809 | **-1,816,441 (-47%)** |
| Turns | 87 | 72 | **-15 (-17%)** |
| Tool calls | 121 | 105 | -16 (-13%) |
| Failed tool calls | 46 | 22 | **-24 (-52%)** |
| Duration ms | 822,625 | 567,066 | **-255,559 (-31%)** |
| Est. cost USD | $12.24 | $6.58 | **-$5.66 (-46%)** |

Biggest per-stage wins: docs −75% input tokens; implement −50%; review −39%.

### Success criteria

- [x] Review/docs/deliver receive PR-first context when known.
- [x] Prompt size decreases or stays flat while useful context improves. (**-47% total pipeline input tokens**)
- [x] Discovery-related tool calls decrease. (**failed tool calls −52%; total tool calls −13%**)

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
