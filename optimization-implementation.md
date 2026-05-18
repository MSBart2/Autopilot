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

## Milestone 2: Harness system prompt spike

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
| TBD | Use stage-level metrics as primary optimization unit | Stage totals reveal bottlenecks better than run totals | Metrics model changes |
| TBD | Start with PR-first review for prompt/context A/B tests | It isolates review routing and avoids full pipeline noise | Full issue baselines are available |

## Open questions

- Which exact workflows should be the first deterministic promotion candidates after PR-first context?
- Should prompt/system-message A/B be stored per run, per stage, or only in logs?
- What minimum sample size is enough before enabling an optimization by default?
- Should baseline scenarios live as actual seeded test issues/PRs in a sandbox repo?
- How should we normalize quality outcomes so token reductions do not hide worse review decisions?
