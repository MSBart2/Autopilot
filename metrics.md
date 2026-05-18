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
| `baseline-aspire-docs` | [#32 Observability runbook](https://github.com/MSBart2/aspire1/issues/32) | Small / docs | Low-complexity baseline; minimal code noise |
| `baseline-aspire-helper` | [#33 Weather summary helper](https://github.com/MSBart2/aspire1/issues/33) | Medium / code + tests | Discovery, implementation, validation, review |
| `baseline-aspire-ui` | [#34 Diagnostics panel](https://github.com/MSBart2/aspire1/issues/34) | Larger / cross-cutting | Architecture discovery, UI, feature flags, review |
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
- Record:
  - `git rev-parse HEAD` in the Cyberpilot repo
  - `git rev-parse HEAD` in the Aspire1 repo

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

For raw extraction, query `web\cyberpilot.db`:

```sql
-- Per-stage detail for one run
SELECT
  r.Id AS RunId,
  r.Repository,
  r.IssueNumber,
  r.IssueTitle,
  r.Model AS RequestedModel,
  r.SkipDeliver,
  r.Status AS RunStatus,
  r.CreatedAt,
  r.CompletedAt,
  l.StageName,
  l.Status AS StageStatus,
  l.Model AS ReportedModel,
  l.SelectedModel,
  l.FallbackModel,
  l.InputTokens,
  l.OutputTokens,
  l.CacheReadTokens,
  l.CacheWriteTokens,
  l.ReasoningTokens,
  l.PremiumRequestCost,
  l.EstimatedCostUsd,
  l.DurationMs,
  l.TurnCount,
  l.ToolCallCount,
  l.FailedToolCallCount,
  l.SessionErrorCount,
  l.ReachedIdle,
  l.WasAborted
FROM PipelineRuns r
JOIN PipelineStageLogs l ON l.RunId = r.Id
WHERE r.Id = '<RUN_ID>'
ORDER BY l.StartedAt;
```

```sql
-- Run totals for all runs on one benchmark issue
SELECT
  r.Id AS RunId,
  r.CreatedAt,
  r.Status,
  SUM(COALESCE(l.InputTokens, 0))        AS InputTokens,
  SUM(COALESCE(l.OutputTokens, 0))       AS OutputTokens,
  SUM(COALESCE(l.CacheReadTokens, 0))    AS CacheReadTokens,
  SUM(COALESCE(l.CacheWriteTokens, 0))   AS CacheWriteTokens,
  SUM(COALESCE(l.DurationMs, 0))         AS DurationMs,
  SUM(COALESCE(l.TurnCount, 0))          AS Turns,
  SUM(COALESCE(l.ToolCallCount, 0))      AS ToolCalls,
  SUM(COALESCE(l.FailedToolCallCount, 0)) AS FailedToolCalls,
  SUM(COALESCE(l.EstimatedCostUsd, 0))   AS EstimatedCostUsd
FROM PipelineRuns r
JOIN PipelineStageLogs l ON l.RunId = r.Id
WHERE r.Repository = 'MSBart2/Aspire1'
  AND r.IssueNumber = <ISSUE_NUMBER>
GROUP BY r.Id, r.CreatedAt, r.Status
ORDER BY r.CreatedAt DESC;
```

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
| | | | | | | | | | | | | | | | | | |

## Experiment results log

| Experiment | Scenario | Before run | After run | Input token Δ | Turn Δ | Duration Δ ms | Reliability | Decision |
| --- | --- | --- | --- | ---: | ---: | ---: | --- | --- |
| | | | | | | | | |
