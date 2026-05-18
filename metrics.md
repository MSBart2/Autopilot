# Cyberpilot Optimization Metrics

How to baseline, run, and compare Cyberpilot optimization experiments. Implementation tasks live in `optimization-implementation.md`. The strategic plan is in `optimization-plan.md`.

## Outcome goals

| Dimension | Goal | Primary metrics |
| --- | --- | --- |
| Token efficiency | Reduce wasted prompt/tool context without losing quality | input tokens, output tokens, cache read/write tokens |
| Turn efficiency | Reduce model loops spent rediscovering deterministic state | assistant turn count, tool call count, repeated discovery steps |
| Runtime | Shorten stage and pipeline wall-clock time | stage duration, total run duration, slowest stage |
| Reliability | Reduce invalid stage results and avoidable failures | valid JSON rate, artifact validation failures, retry count, session errors |
| Determinism | Move repeatable mechanics into code/tools/gates | deterministic tool usage count, manual shell/API discovery count |
| Safety | Limit tool blast radius and redact noisy/sensitive output | denied risky tool calls, failed tool calls, redaction events |

## What is already captured in the database

Most quantitative metrics are available from `web\cyberpilot.db` without any extra instrumentation.

| Table | Useful fields |
| --- | --- |
| `PipelineRuns` | `Id`, `Repository`, `IssueNumber`, `IssueTitle`, `Model`, `Status`, `CreatedAt`, `CompletedAt`, `SkipDeliver`, `PipelineDefinitionName`, `PolicyProfileName` |
| `PipelineStageLogs` | `RunId`, `StageName`, `Status`, `StartedAt`, `CompletedAt`, `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheWriteTokens`, `ReasoningTokens`, `PremiumRequestCost`, `EstimatedCostUsd`, `DurationMs`, `TurnCount`, `ToolCallCount`, `FailedToolCallCount`, `SessionErrorCount`, `ReachedIdle`, `WasAborted`, `Model`, `SelectedModel`, `FallbackModel` |
| `PipelineArtifacts` / `PipelineEvidence` | Structured outputs and evidence for qualitative comparison |

The database covers the core before/after dimensions: tokens, cache tokens, turns, tool calls, failed calls, model duration, estimated cost, model/fallback selection, stage and run status.

Manual notes remain useful for:

- whether a generated implementation was actually acceptable
- whether cleanup/reset was performed between runs
- any operator intervention that invalidates the run
- model thrash, repeated searches, or incorrect assumptions not obvious from counts alone
- PR/review quality observations

## Benchmark issues

These issues in `MSBart2/Aspire1` are scoped to give useful signal without delivery.

| Scenario ID | Issue | Complexity | Purpose |
| --- | --- | --- | --- |
| `baseline-aspire-ui` | [#32 Diagnostics panel](https://github.com/MSBart2/aspire1/issues/32) | Larger / cross-cutting | Architecture discovery, UI, feature flags, review |
| `baseline-aspire-helper` | [#33 Weather summary helper](https://github.com/MSBart2/aspire1/issues/33) | Medium / code + tests | Discovery, implementation, validation, review |
| `baseline-aspire-docs` | [#34 Observability runbook](https://github.com/MSBart2/aspire1/issues/34) | Small / docs | Low-complexity baseline; minimal code noise |
| `baseline-pr-review` | TBD PR from a benchmark run | — / PR-first review | Review → docs → deliver without issue routing overhead |

## Recommended baseline cadence

| Step | Runs | Notes |
| --- | ---: | --- |
| Initial before baseline | 2 per benchmark issue | Enough to expose variance without burning quota |
| Optimization smoke test | 1 on #32 | Start cheap, check behavior |
| Optimization full test | 1 each on #33 and #34 | Broaden to code and UI scenarios |
| After baseline | 2 per benchmark issue | Compare against initial before baseline |
| PR-first review baseline | 2 review-only runs | Best for prompt/context changes targeting review routing |

## Step-by-step benchmark procedure

### 1. Prepare

- Pick a benchmark issue from the table above.
- Confirm the Aspire1 local repo path is configured in Cyberpilot.
- Confirm the working tree / worktree is clean.
- Fix the comparison settings:
  - model
  - stage timeout
  - pipeline definition
  - policy profile
  - skip delivery enabled

> **SHAs are captured automatically.** Cyberpilot records `CyberpilotSha` and `TargetRepoSha` in `PipelineRuns` at run start — no manual `git rev-parse` needed. Both appear in the per-stage query output.

### 2. Dispatch

- Open the Launch Board.
- Select `MSBart2/Aspire1`.
- Select the benchmark issue.
- Enable skip-deliver.
- Use the fixed comparison model and timeout.
- Copy the Cyberpilot run ID from the Run Room immediately after dispatch — this is the database join key.

### 3. Observe

- Let the run complete or halt naturally.
- Do not steer, retry, or correct unless the experiment is specifically testing intervention.
- Intervention invalidates the run as baseline data; note it and start again.
- Leave any generated PR/branch in place until metrics are captured.

### 4. Capture database metrics

Check the Run Room first; it shows stage-level tokens, turns, tool calls, duration, model, and status.

For raw extraction, use the scripts in `scripts/` — both require `sqlite3` on your PATH.

**Per-stage detail for a single run** (use immediately after dispatch, replace `<RUN_ID>` with the ID from the Run Room):

```powershell
.\scripts\Get-RunMetrics.ps1 -RunId "<RUN_ID>"
```

**All runs for a benchmark issue** (use for before/after comparison):

```powershell
.\scripts\Get-IssueBenchmarks.ps1 -IssueNumber 32
.\scripts\Get-IssueBenchmarks.ps1 -IssueNumber 33
.\scripts\Get-IssueBenchmarks.ps1 -IssueNumber 34
```

Both scripts default to `web\cyberpilot.db` and `MSBart2/Aspire1`. Pass `-DbPath` or `-Repository` to override.

### 5. Capture qualitative notes

For each run, record:

- Did the model understand the issue without operator help?
- Did it repeatedly search for facts the harness already knows?
- Did it choose appropriate validation commands?
- Did it produce a usable implementation or stop for a sensible reason?
- Were there environment problems that invalidate the run?

### 6. Reset before repeating

- Close or delete the generated PR/branch.
- Reset the Aspire1 working tree to the recorded starting SHA.
- Reset Cyberpilot settings to the fixed comparison values.
- Note the reset action in the run log.

### 7. Compare before vs. after

- Re-run the same benchmark issues with the same settings after implementing the optimization.
- Pull the same queries.
- Compare stage-level and run-level deltas:
  - tokens
  - turns
  - tool calls
  - duration
  - estimated cost
  - failed tool calls and session errors
  - valid JSON/artifact validity
- Do not count a run as an improvement if token/time metrics improve but correctness or quality regresses.

## Measurement rules

- Always capture a baseline before enabling an optimization by default.
- Use the same model, stage timeout, pipeline definition, policy profile, and repository for before/after comparisons.
- Prefer stage-level over run-level metrics — bottlenecks are only visible at stage granularity.
- Record failed or noisy experiments too; bad data is still data.
- Run benchmark issues with delivery disabled unless the experiment explicitly targets delivery behavior.

## Baseline data log

Copy one row per stage per run. Most values come from the database queries above; Cyberpilot SHA and Aspire1 SHA must be recorded manually before dispatch.

| Run ID | Scenario | Stage | Model | Cyberpilot SHA | Aspire1 SHA | Input tokens | Output tokens | Cache read | Cache write | Turns | Tool calls | Failed calls | Duration ms | Est. cost USD | Valid JSON? | Artifact valid? | Notes |
| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- |
| 84f798d700f34998906e6d85e7d38bde | baseline-aspire-ui (#32) | triage | claude-sonnet-4.6 | d40d3c2 | df3abae | 453,418 | 4,897 | 408,513 | 0 | 12 | 19 | 7 | 113,201 | $1.4337 | | | Run 1 — **full delivery** (skip-deliver bug) |
| 84f798d700f34998906e6d85e7d38bde | baseline-aspire-ui (#32) | plan | claude-sonnet-4.6 | d40d3c2 | df3abae | 820,454 | 10,748 | 759,679 | 0 | 17 | 35 | 19 | 218,745 | $2.6226 | | | |
| 84f798d700f34998906e6d85e7d38bde | baseline-aspire-ui (#32) | implement (1) | claude-sonnet-4.6 | d40d3c2 | df3abae | 1,590,580 | 13,757 | 1,530,552 | 0 | 30 | 41 | 17 | 236,872 | $4.9781 | | | Review requested changes → rework |
| 84f798d700f34998906e6d85e7d38bde | baseline-aspire-ui (#32) | review (1) | claude-sonnet-4.6 | d40d3c2 | df3abae | 548,042 | 14,933 | 492,910 | 0 | 11 | 17 | 10 | 294,423 | $1.8681 | | | Changes requested |
| 84f798d700f34998906e6d85e7d38bde | baseline-aspire-ui (#32) | implement (2) | claude-sonnet-4.6 | d40d3c2 | df3abae | 1,192,763 | 9,823 | 1,134,431 | 0 | 23 | 35 | 14 | 196,635 | $3.7256 | | | |
| 84f798d700f34998906e6d85e7d38bde | baseline-aspire-ui (#32) | review (2) | claude-sonnet-4.6 | d40d3c2 | df3abae | 604,907 | 9,763 | 546,548 | 0 | 14 | 24 | 12 | 197,900 | $1.9612 | | | Approved |
| 84f798d700f34998906e6d85e7d38bde | baseline-aspire-ui (#32) | docs | claude-sonnet-4.6 | d40d3c2 | df3abae | 1,156,106 | 10,375 | 1,097,041 | 0 | 21 | 33 | 16 | 203,533 | $3.6239 | | | |
| 84f798d700f34998906e6d85e7d38bde | baseline-aspire-ui (#32) | deliver | claude-sonnet-4.6 | d40d3c2 | df3abae | 293,203 | 4,600 | 261,169 | 0 | 8 | 9 | 5 | 107,281 | $0.9486 | | | Ran due to skip-deliver bug |
| | **TOTALS** | | | | | **6,659,473** | **78,896** | **6,230,843** | **0** | **136** | **213** | **100** | **1,568,590** | **$21.16** | | | SkipDeliver=false (bug). Full delivery. 2 review cycles. |

## Experiment results log

| Experiment | Scenario | Before run | After run | Input token Δ | Turn Δ | Duration Δ ms | Reliability | Decision |
| --- | --- | --- | --- | ---: | ---: | ---: | --- | --- |
| | | | | | | | | |
