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
| `baseline-pr-review` | Fresh cloned PR fixtures from #34 implementation commit (`review5clone-20260518-2030-*`) | — / PR-first review | Review-only comparison without issue routing overhead |

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
| edd0d6eae50d46558cf3e53093f9d672 | baseline-aspire-ui (#32) | triage | claude-sonnet-4.6 | a81f824 | 8a4cadc | 326,516 | 4,760 | 275,241 | 0 | 8 | 16 | 7 | 102,850 | $1.0509 | | | Run 2 |
| edd0d6eae50d46558cf3e53093f9d672 | baseline-aspire-ui (#32) | plan | claude-sonnet-4.6 | a81f824 | 8a4cadc | 600,431 | 13,954 | 545,895 | 0 | 14 | 26 | 11 | 279,911 | $2.0106 | | | |
| edd0d6eae50d46558cf3e53093f9d672 | baseline-aspire-ui (#32) | implement | claude-sonnet-4.6 | a81f824 | 8a4cadc | 1,872,397 | 14,153 | 1,811,224 | 0 | 35 | 46 | 12 | 264,846 | $5.8295 | | | |
| edd0d6eae50d46558cf3e53093f9d672 | baseline-aspire-ui (#32) | review | claude-sonnet-4.6 | a81f824 | 8a4cadc | 318,057 | 6,024 | 280,005 | 0 | 8 | 13 | 5 | 123,087 | $1.0445 | | | Approved |
| edd0d6eae50d46558cf3e53093f9d672 | baseline-aspire-ui (#32) | docs | claude-sonnet-4.6 | a81f824 | 8a4cadc | 1,270,548 | 8,914 | 1,221,176 | 0 | 26 | 32 | 8 | 163,378 | $3.9454 | | | |
| | **TOTALS** | | | | | **4,387,949** | **47,805** | **4,133,541** | **0** | **91** | **133** | **43** | **934,072** | **$13.88** | | | SkipDeliver=true. 1 review cycle. |
| 50b6b02138094e97ac84ae41671e9026 | baseline-aspire-helper (#33) | triage |claude-sonnet-4.6 | a81f824 | 8a4cadc | 183,325 | 3,228 | 143,578 | 0 | 5 | 6 | 2 | 73,439 | $0.5984 | | | Run 2 |
| 50b6b02138094e97ac84ae41671e9026 | baseline-aspire-helper (#33) | plan | claude-sonnet-4.6 | a81f824 | 8a4cadc | 490,504 | 7,793 | 442,672 | 0 | 11 | 17 | 8 | 166,174 | $1.5884 | | | |
| 50b6b02138094e97ac84ae41671e9026 | baseline-aspire-helper (#33) | implement | claude-sonnet-4.6 | a81f824 | 8a4cadc | 627,580 | 6,540 | 571,697 | 0 | 13 | 17 | 9 | 139,990 | $1.9808 | | | |
| 50b6b02138094e97ac84ae41671e9026 | baseline-aspire-helper (#33) | review | claude-sonnet-4.6 | a81f824 | 8a4cadc | 677,487 | 8,756 | 626,457 | 0 | 14 | 22 | 11 | 172,804 | $2.1638 | | | Approved |
| 50b6b02138094e97ac84ae41671e9026 | baseline-aspire-helper (#33) | docs | claude-sonnet-4.6 | a81f824 | 8a4cadc | 221,389 | 3,457 | 188,752 | 0 | 6 | 8 | 2 | 74,054 | $0.7160 | | | |
| | **TOTALS** | | | | | **2,200,285** | **29,774** | **1,973,156** | **0** | **49** | **70** | **32** | **626,461** | **$7.05** | | | SkipDeliver=true. 1 review cycle. |
| 724eb0c39c5940b5b8e67be0eca1d06a | baseline-aspire-docs (#34) | triage |claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 257,663 | 3,636 | 210,728 | 0 | 7 | 12 | 6 | 74,222 | $0.8275 | | | Run 1 |
| 724eb0c39c5940b5b8e67be0eca1d06a | baseline-aspire-docs (#34) | plan | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 563,743 | 9,957 | 517,837 | 0 | 13 | 26 | 11 | 188,522 | $1.8406 | | | |
| 724eb0c39c5940b5b8e67be0eca1d06a | baseline-aspire-docs (#34) | implement | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 1,121,720 | 11,000 | 1,072,668 | 0 | 25 | 30 | 10 | 196,739 | $3.5302 | | | |
| 724eb0c39c5940b5b8e67be0eca1d06a | baseline-aspire-docs (#34) | review | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 552,044 | 10,297 | 512,849 | 0 | 14 | 18 | 7 | 196,873 | $1.8106 | | | Approved |
| 724eb0c39c5940b5b8e67be0eca1d06a | baseline-aspire-docs (#34) | docs | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 1,370,080 | 8,277 | 1,315,879 | 0 | 28 | 35 | 12 | 166,269 | $4.2344 | | | |
| | **TOTALS** | | | | | **3,865,250** | **43,167** | **3,629,961** | **0** | **87** | **121** | **46** | **822,625** | **$12.24** | | | SkipDeliver=true. 1 review cycle. |
| 64b7bb5ebcee4edeb7defad76bbd0e6b | m3-full (#34) | triage | claude-sonnet-4.6 | 6842057 | 8a4cadc | 162,939 | 3,633 | 143,537 | 0 | 10 | 16 | 3 | 78,443 | $0.5433 | ✅ | ✅ | M2+M3 defaults active |
| 64b7bb5ebcee4edeb7defad76bbd0e6b | m3-full (#34) | plan | claude-sonnet-4.6 | 6842057 | 8a4cadc | 641,286 | 6,505 | 593,987 | 0 | 16 | 25 | 7 | 121,224 | $2.0214 | ✅ | ✅ | |
| 64b7bb5ebcee4edeb7defad76bbd0e6b | m3-full (#34) | implement | claude-sonnet-4.6 | 6842057 | 8a4cadc | 560,517 | 7,714 | 537,220 | 0 | 22 | 27 | 2 | 139,090 | $1.7973 | ✅ | ✅ | |
| 64b7bb5ebcee4edeb7defad76bbd0e6b | m3-full (#34) | review | claude-sonnet-4.6 | 6842057 | 8a4cadc | 338,649 | 5,625 | 298,858 | 0 | 8 | 15 | 7 | 118,267 | $1.1003 | ✅ | ✅ | Approved. PR #51 |
| 64b7bb5ebcee4edeb7defad76bbd0e6b | m3-full (#34) | docs | claude-sonnet-4.6 | 6842057 | 8a4cadc | 345,418 | 5,589 | 326,385 | 0 | 16 | 22 | 3 | 110,042 | $1.1201 | ✅ | ✅ | |
| | **TOTALS** | | | | | **2,048,809** | **29,066** | **1,899,987** | **0** | **72** | **105** | **22** | **567,066** | **$6.58** | | | SkipDeliver=true. 1 review cycle. M2+M3 active. |
| 6faf8925253d425fb1dd911c2f117de5 | m4-review-diff-summary (#34/#51) | review | claude-sonnet-4.6 | 7187117 | 55aa422 | 465,748 | 9,060 | 398,398 | 0 | 9 | 35 | 8 | 200,783 | $1.5331 | ✅ | ✅ | First M4 attempt: typed diff tool promoted, but agent-file guidance was too soft. |
| e08a8865eb3a4c4fae4fe0e946094fd0 | m4-review-diff-summary-guided (#34/#51) | review | claude-sonnet-4.6 | 7cacdbd | 55aa422 | 323,328 | 4,799 | 289,526 | 0 | 9 | 14 | 7 | 103,064 | $1.0420 | ✅ | ✅ | SDK wrapper requires deterministic PR tools before manual diff discovery. |
| ca98c30595f443f3a044da5497bb68a7 | m4-review-comment-renderer (#34/#51) | review | claude-sonnet-4.6 | 009e27c | 55aa422 | 418,455 | 6,231 | 370,300 | 0 | 11 | 21 | 8 | 141,359 | $1.3488 | ✅ | ✅ | First renderer attempt: removed denied write attempts but renderer output was too large and failed once. |
| b6fb7ee7edce4ff7968555191cb0d959 | m4-review-comment-renderer-compact (#34/#51) | review | claude-sonnet-4.6 | a1d422b | 55aa422 | 414,376 | 5,980 | 377,240 | 0 | 11 | 19 | 6 | 124,775 | $1.3328 | ✅ | ✅ | Compact renderer: 0 denied writes, 0 renderer failures; token cost not improved vs guided diff baseline. |
| 9e82c517258b4b9985a5682f7dad6d55 | baseline-aspire-helper (#33) | triage |claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 269,817 | 3,965 | 225,276 | 0 | 7 | 10 | 4 | 86,361 | $0.8689 | | | Run 1 |
| 9e82c517258b4b9985a5682f7dad6d55 | baseline-aspire-helper (#33) | plan | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 541,785 | 7,919 | 496,514 | 0 | 12 | 19 | 10 | 156,557 | $1.7441 | | | |
| 9e82c517258b4b9985a5682f7dad6d55 | baseline-aspire-helper (#33) | implement (1) | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 640,796 | 6,313 | 598,810 | 0 | 15 | 17 | 9 | 143,598 | $2.0171 | | | |
| 9e82c517258b4b9985a5682f7dad6d55 | baseline-aspire-helper (#33) | review (1) | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 716,101 | 10,451 | 662,616 | 0 | 16 | 29 | 6 | 193,948 | $2.3051 | | | Changes requested |
| 9e82c517258b4b9985a5682f7dad6d55 | baseline-aspire-helper (#33) | implement (2) | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 629,676 | 4,945 | 593,346 | 0 | 16 | 23 | 4 | 93,894 | $1.9632 | | | |
| 9e82c517258b4b9985a5682f7dad6d55 | baseline-aspire-helper (#33) | review (2) | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 653,285 | 9,189 | 600,566 | 0 | 15 | 26 | 8 | 177,306 | $2.0977 | | | Approved |
| 9e82c517258b4b9985a5682f7dad6d55 | baseline-aspire-helper (#33) | docs | claude-sonnet-4.6 | d1c3d35 | 8a4cadc | 200,996 | 4,382 | 163,708 | 0 | 5 | 7 | 3 | 82,680 | $0.6687 | | | |
| | **TOTALS** | | | | | **3,652,456** | **47,164** | **3,340,836** | **0** | **86** | **131** | **44** | **934,344** | **$11.66** | | | SkipDeliver=true. 2 review cycles. |
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
| Milestone 2 triage system-message sweep | #34 one-shot triage | `triage5-20260518-1936-inline-full` | `triage5-20260518-1936-replace-lean` | -138,219 | +2 | -9,890 | 5/5 valid JSON; all approved | Use `replace-lean` for triage. |
| Milestone 2 plan seeded sweep | #34 plan with fixed triage seed | `plan5-20260518-2009-replace-lean` | `plan5-20260518-2009-inline-full` | -47,514 | -2 | -20,780 | 5/5 valid JSON; all approved | Use `inline-full` for plan. |
| Milestone 2 PR-first review sweep | Fresh PR clones #46-#50 at same commit | `review5clone-20260518-2030-inline-full` | `review5clone-20260518-2030-append-lean` | -210,611 | -6 | -64,776 | 5/5 valid JSON; all approved | Use `append-lean` for review. |
| Milestone 2+3 full pipeline (#34) | #34 full flow, M2+M3 active vs pre-M2 baseline | `724eb0c` (pre-M2) | `64b7bb5` (M2+M3) | -1,816,441 (-47%) | -15 (-17%) | -255,559 (-31%) | 5/5 GO; all approved; 22 vs 46 failed tool calls (-52%) | M2+M3 combined. Docs alone: -1,024,662 tokens (-75%). Review: -213,395 (-39%). Implement: -561,203 (-50%). |
| Milestone 4 PR diff-summary promotion | #34/#51 review-only, same PR head | `m4-review-diff-summary-20260519` | `m4-review-diff-summary-guided-20260519` | -142,420 (-31%) | 0 | -97,719 (-49%) | 2/2 GO; guided run cut tool calls 35→14 and failed calls 8→7 | Agent-file guidance was too soft; SDK wrapper-level deterministic PR tool guidance is required. Guided run also beat M3 review baseline by -15,321 tokens and -15,203 ms. |
| Milestone 4 stage-comment renderer | #34/#51 review-only, same PR head | `m4-review-comment-renderer-20260519` | `m4-review-comment-renderer-compact-20260519` | -4,079 (-1%) | 0 | -16,584 (-12%) | 2/2 GO; denied GitHub writes 0/0; renderer failures 1→0 | Keep `render_stage_comment` for safety/reliability, not token savings. Compact cap fixed long-summary tool failure. |

### Milestone 2 stage prompt benchmark details

#### Triage: issue #34 one-shot sweep (`triage5-20260518-1936-*`)

| Variant | Status | Duration ms | Input tokens | Output tokens | Cache read | Turns | Tool calls | Failed calls | Premium cost | Result |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| inline-full | GO | 77,367 | 307,770 | 3,650 | 272,780 | 8 | 11 | 3 | 8 | approved / valid |
| append-full | GO | 94,761 | 282,812 | 4,511 | 249,468 | 8 | 16 | 5 | 8 | approved / valid |
| replace-full | GO | 79,211 | 151,161 | 3,858 | 134,539 | 8 | 16 | 5 | 8 | approved / valid |
| append-lean | GO | 73,534 | 238,195 | 3,851 | 208,411 | 7 | 13 | 4 | 7 | approved / valid |
| replace-lean | GO | 67,477 | 169,551 | 3,363 | 157,308 | 10 | 15 | 4 | 10 | approved / valid |

#### Plan: issue #34 with fixed triage seed (`plan5-20260518-2009-*`)

Seed: `planseed-triage-replace-lean-20260518-2009` completed GO / approved / valid in 79,872 ms with 149,402 input tokens. Each plan run loaded that exact triage `StageResultJson` into stage history.

| Variant | Status | Duration ms | Input tokens | Output tokens | Cache read | Turns | Tool calls | Failed calls | Premium cost | Result |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| inline-full | GO | 96,820 | 147,576 | 5,376 | 134,765 | 9 | 15 | 3 | 9 | approved / valid |
| append-full | GO | 120,836 | 251,435 | 7,136 | 215,603 | 7 | 17 | 4 | 7 | approved / valid |
| replace-full | GO | 130,140 | 153,494 | 7,146 | 137,964 | 9 | 16 | 4 | 9 | approved / valid |
| append-lean | GO | 146,338 | 329,376 | 8,181 | 291,138 | 9 | 17 | 7 | 9 | approved / valid |
| replace-lean | GO | 117,600 | 195,090 | 6,268 | 180,363 | 11 | 17 | 5 | 11 | approved / valid |

#### PR-first review: fresh PR clones at same fixture commit (`review5clone-20260518-2030-*`)

Fixture source: issue #34 implementation PR #45 produced commit `d0461c8fe462718aa740787521609e1a5873d85f`. PRs #46-#50 were fresh branches pointing at the same commit so each review run started with equivalent PR state. The fixture PRs and branches were closed/deleted after metrics capture.

| Variant | PR | Status | Duration ms | Input tokens | Output tokens | Cache read | Turns | Tool calls | Failed calls | Premium cost | Result |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| inline-full | #46 | GO | 169,168 | 496,272 | 7,682 | 460,298 | 14 | 19 | 8 | 14 | approved / valid |
| append-full | #47 | GO | 183,083 | 507,891 | 8,676 | 463,577 | 13 | 22 | 9 | 13 | approved / valid |
| replace-full | #48 | GO | 159,641 | 263,521 | 7,828 | 242,957 | 15 | 22 | 8 | 15 | approved / valid |
| append-lean | #49 | GO | 104,392 | 285,661 | 4,716 | 250,868 | 8 | 13 | 5 | 8 | approved / valid |
| replace-lean | #50 | GO | 173,126 | 323,513 | 8,478 | 303,536 | 17 | 21 | 10 | 17 | approved / valid |

Prompt-size note: Cyberpilot does not currently persist raw prompt character count per run. The persisted input-token totals above are the operational prompt/context-size proxy for Milestone 2 comparisons.

### Milestone 3 full pipeline benchmark (#34, `m3-full-20260518`)

Run ID: `64b7bb5ebcee4edeb7defad76bbd0e6b`. Cyberpilot SHA `6842057`, Aspire1 SHA `8a4cadc`. All M2 defaults active (triage=replace-lean, plan=inline-full, review=append-lean) plus M3 typed `StageContextSnapshot`. Compared against M2 baseline run `724eb0c39c5940b5b8e67be0eca1d06a` (Cyberpilot SHA `d1c3d35`).

| Stage | Before input | After input | Δ input | Before turns | After turns | Δ turns | Before failed | After failed | Δ failed | Before dur ms | After dur ms | Δ dur ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| triage | 257,663 | 162,939 | -94,724 (-37%) | 7 | 10 | +3 | 6 | 3 | -3 | 74,222 | 78,443 | +4,221 |
| plan | 563,743 | 641,286 | +77,543 (+14%) | 13 | 16 | +3 | 11 | 7 | -4 | 188,522 | 121,224 | -67,298 |
| implement | 1,121,720 | 560,517 | -561,203 (-50%) | 25 | 22 | -3 | 10 | 2 | -8 | 196,739 | 139,090 | -57,649 |
| review | 552,044 | 338,649 | -213,395 (-39%) | 14 | 8 | -6 | 7 | 7 | 0 | 196,873 | 118,267 | -78,606 |
| docs | 1,370,080 | 345,418 | -1,024,662 (-75%) | 28 | 16 | -12 | 12 | 3 | -9 | 166,269 | 110,042 | -56,227 |
| **TOTALS** | **3,865,250** | **2,048,809** | **-1,816,441 (-47%)** | **87** | **72** | **-15 (-17%)** | **46** | **22** | **-24 (-52%)** | **822,625** | **567,066** | **-255,559 (-31%)** |

Note: triage and plan input-token increases are within normal variance and partially reflect the new typed JSON context adding structured fields. The net pipeline improvement is strongly positive — implement, review, and docs all benefit substantially from receiving compact machine-readable context instead of rediscovering state via shell/API calls.

### Milestone 4 PR diff-summary promotion (`m4-review-diff-summary-*`)

Fixture: issue #34 PR #51 at head commit `55aa4226a05803984a5f203525a86a2078e94c57`. The first M4 attempt promoted richer typed `get_pr_diff_summary` output and updated review/docs agent files, but the model still improvised diff/file discovery. The guided run added SDK wrapper-level deterministic PR-tool instructions for review/docs/deliver before rerunning the same review stage.

| Run | Cyberpilot SHA | Input tokens | Output tokens | Cache read | Turns | Tool calls | Failed calls | Duration ms | Cost | Result |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| M3 review baseline (`m3-full-20260518`) | `6842057` | 338,649 | 5,625 | 298,858 | 8 | 15 | 7 | 118,267 | $1.1003 | GO / approved |
| M4 first attempt (`m4-review-diff-summary-20260519`) | `7187117` | 465,748 | 9,060 | 398,398 | 9 | 35 | 8 | 200,783 | $1.5331 | GO / approved |
| M4 guided (`m4-review-diff-summary-guided-20260519`) | `7cacdbd` | 323,328 | 4,799 | 289,526 | 9 | 14 | 7 | 103,064 | $1.0420 | GO / approved |

Takeaway: simply improving the tool and agent files was not enough; the stage still used absolute-path file reads, denied subagent/task calls, and more tool calls. Moving deterministic PR tool instructions into the SDK prompt wrapper made the workflow stick: compared with the first M4 attempt, guided review reduced input tokens by 142,420 (-31%), tool calls by 21 (-60%), and duration by 97,719 ms (-49%). Compared with the M3 review baseline, guided review is modestly better on input tokens (-15,321 / -5%), tool calls (-1), duration (-15,203 ms / -13%), and cost (-$0.0584 / -5%).

### Milestone 4 stage-comment renderer (`m4-review-comment-renderer-*`)

Fixture: same issue #34 PR #51 head commit `55aa4226a05803984a5f203525a86a2078e94c57`. This promoted `render_stage_comment`, a deterministic no-write tool that renders started/progress/verdict/verification/landing Markdown for stage artifacts instead of having read-only stages attempt GitHub comments or reviews.

| Run | Cyberpilot SHA | Input tokens | Output tokens | Cache read | Turns | Tool calls | Failed calls | Duration ms | Cost | Failure profile | Result |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| M4 guided baseline (`m4-review-diff-summary-guided-20260519`) | `7cacdbd` | 323,328 | 4,799 | 289,526 | 9 | 14 | 7 | 103,064 | $1.0420 | 4 view path failures, 2 denied writes, 1 PowerShell failure | GO / approved |
| Renderer first attempt (`m4-review-comment-renderer-20260519`) | `009e27c` | 418,455 | 6,231 | 370,300 | 11 | 21 | 8 | 141,359 | $1.3488 | 6 view path failures, 1 PowerShell failure, 1 renderer failure | GO / approved |
| Compact renderer (`m4-review-comment-renderer-compact-20260519`) | `a1d422b` | 414,376 | 5,980 | 377,240 | 11 | 19 | 6 | 124,775 | $1.3328 | 5 view path failures, 1 PowerShell failure, 0 denied writes, 0 renderer failures | GO / approved |

Takeaway: `render_stage_comment` is a safety/reliability promotion, not a token win. It eliminated denied durable-write attempts and, after adding a compact summary cap, eliminated renderer tool failures. The next likely M4 target should address the remaining repeated failures: absolute-path file reads and ad hoc validation commands.
