# Cyberpilot Architecture

Technical reference for the pipeline-first Cyberpilot repository.

## Solution Overview

| Property | Value |
|----------|-------|
| Web framework | ASP.NET Core MVC (.NET 10) |
| Web SDK | `Microsoft.NET.Sdk.Web` |
| Web root namespace | `Cyberpilot.Web` |
| SDK library | [copilot-sdk/Cyberpilot.Sdk.csproj](copilot-sdk/Cyberpilot.Sdk.csproj) |
| SDK executable | [copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj](copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj) |
| Web entry point | [web/Program.cs](web/Program.cs) |
| Versioning | Semantic version read from [VERSION](VERSION) at build time |

## Architectural Intent

Cyberpilot is no longer a broad MVC demo lab. The web project is a small support surface for the AI-SDLC pipeline. Pipeline behavior is owned by:

- Local custom agents in [.github/agents/](.github/agents)
- GitHub Agentic Workflow sources in [.github/workflows/](.github/workflows)
- The .NET SDK runner library in [copilot-sdk/](copilot-sdk) and console harness in [copilot-sdk-exe/](copilot-sdk-exe)

The web app intentionally avoids session state, custom middleware, feature-admin UI, Swagger, telemetry packages, Redis, and demo APIs. Shared SDK persistence keeps SQLite history for Cyberpilot runs and stage logs.

## Folder Layout

```text
web/                            ASP.NET Core MVC pipeline portal
web/Controllers/                MVC controllers
web/Models/                     Pipeline, run, and error view models
web/Views/                      Razor portal, issue launcher, and run detail views
web/Services/                   Web-triggered SDK runner queue and background service
web/Hubs/                       SignalR hub for pipeline progress
web/wwwroot/                    Static assets and SignalR client library
.github/agents/                 Local custom agents
.github/workflows/              Cloud agentic workflow sources and locks
copilot-sdk/                    Programmatic SDK runner library and shared persistence
copilot-sdk/Persistence/        EF Core context, run entities, and migrations
copilot-sdk-exe/                Console EXE harness for SDK runs
tests/Cyberpilot.Web.UnitTests/  Focused controller tests
tests/Cyberpilot.Web.IntegrationTests/ Web smoke tests
tests/Cyberpilot.Sdk.Tests/      SDK runner tests
docs/                           Operational and pipeline docs
```

## Web Project

The web project references the SDK runner and SDK-owned EF Core SQLite persistence for run history. Schema changes are managed by EF Core migrations under [copilot-sdk/Persistence/Migrations/](copilot-sdk/Persistence/Migrations).

Registered services in [web/Program.cs](web/Program.cs):

| Service | Purpose |
|---------|---------|
| `AddControllersWithViews` | MVC pipeline portal |
| `AddSignalR` | Live pipeline run updates at `/pipelineHub` |
| `AddHealthChecks` | Readiness endpoint at `/health/ready` |
| `CyberpilotDbContext` | SDK-owned migrated SQLite pipeline runs and stage logs |
| `CyberpilotPipelineService` | Background SDK runner for web-triggered runs |
| `PipelineDefinitionAdminStore` | File-backed editor for operator-managed JSON pipeline definitions and policy profiles |
| `ModelPricingService` | Static class in `Cyberpilot.Persistence`; maps model IDs to per-1M-token USD rates and returns `0` for unknown models. Powers cost estimation in both sink implementations. |

**Stage telemetry flow:** `CopilotStageRunner` subscribes to streaming SDK events while each stage runs. It aggregates assistant turns, `assistant.usage` token/cache/duration data, tool starts/completions, session errors, idle state, provider call IDs, and API call IDs into `StageExecutionMetrics`. Final `session.Rpc.Usage.GetMetricsAsync()` capture remains as a non-fatal fallback for input/output tokens, model duration, and premium request cost. Both sink implementations (`CyberpilotRunHistoryProgressSink` and `SignalRProgressSink`) write the legacy token/cost columns plus rich metrics and `RetryCount` to `PipelineStageLog`.

**Stage artifact flow:** Structured `StageResult.Artifacts` are persisted as first-class `PipelineArtifact` rows by both sink implementations. Artifacts keep the producing run, stage, optional stage log, artifact name, value, URI, media type, contract version, source, and capture time. The run details page loads artifacts directly from `PipelineArtifacts` and renders an artifact ledger independent of raw transcripts and compatibility evidence rows.

**Deterministic SDK tools:** `CopilotStageRunner` attaches harness-owned tools from [copilot-sdk/Copilot/PipelineContextToolProvider.cs](copilot-sdk/Copilot/PipelineContextToolProvider.cs) to every SDK session. The current tools are `get_pipeline_context`, `get_pr_details`, and `get_pr_diff_summary`. They return compact typed results for model consumption and structured errors such as `missing_pr`, `pr_details_failed`, and `pr_diff_summary_failed`. PR tools use `gh pr view --json ...` through the existing GitHub CLI abstraction. `get_pr_diff_summary` returns changed-file stats, top-directory and extension groups, and deterministic review signals such as `production_code_changed`, `test_code_changed`, `documentation_changed`, `web_surface_changed`, and `configuration_changed` so review/docs stages can avoid rediscovering the PR shape with shell commands. Detailed JSON output can be recorded on `PipelineExecutionContext` as `tool-output-*` artifacts when `CaptureToolOutputArtifacts` is enabled; normal runs leave those verbose diagnostics out of `PipelineArtifacts` so the artifact ledger stays focused on stage-owned outputs.

**Stage tool policy hooks:** SDK sessions also attach `StageToolPolicyHooks`, which use `SessionHooks.OnPreToolUse` and `OnPostToolUse`. Pre-tool policy allows Cyberpilot's deterministic read tools, denies broad write-looking operations in read-only stages, and leaves write-capable stages (`implement`, `docs`, `deliver`) able to perform code, documentation, and delivery actions. Post-tool policy redacts secret-looking output and truncates noisy output before it reaches the model context. Shaped output is recorded as `tool-hook-*` artifacts only when `CaptureToolOutputArtifacts` is enabled for diagnostic runs.

**Per-stage model selection:** `StageModelResolver` checks the configured model before each stage starts. The global `--model` remains the default, `--stage-model <stage>=<model>` overrides one stage or `*`, and `--stage-fallback-model <stage>=<model>` provides the fallback used when the configured stage model is unavailable. Web-triggered runs can pass the same maps through request-level stage model override/fallback fields. Selected model, configured model, fallback model, and fallback reason are recorded on each `PipelineStageLog`.

**`PipelineStageLog` columns:**

Token tracking (added in migration `AddTokenUsageToPipelineStageLog`):
- `InputTokens` (INTEGER, nullable) — input tokens for this stage call
- `OutputTokens` (INTEGER, nullable) — output tokens for this stage call
- `EstimatedCostUsd` (REAL, nullable) — computed cost using `ModelPricingService`

Retry tracking (added in migration `AddRetryCountToPipelineStageLog`):
- `RetryCount` (INTEGER, nullable) — attempt index for this stage (0 = first attempt, 1 = first retry, etc.)

The `RetryCount` value is set by both sink implementations (`SignalRProgressSink` and `CyberpilotRunHistoryProgressSink`) by counting existing logs for the same run and stage before inserting the new row.

Execution metric tracking (added in migration `AddStageExecutionMetricsToPipelineStageLog`):
- `Model` (TEXT, nullable) — model that reported stage usage, falling back to the configured run model
- `ConfiguredModel`, `SelectedModel`, `FallbackModel`, `FallbackReason` (TEXT, nullable; added in migration `AddStageModelSelectionToPipelineStageLog`) — per-stage model selection and fallback metadata
- `CacheReadTokens`, `CacheWriteTokens`, `ReasoningTokens` (INTEGER, nullable) — additional usage counters from streaming SDK usage events
- `PremiumRequestCost` (REAL, nullable) — SDK-reported premium request cost or multiplier
- `DurationMs` (REAL, nullable) — accumulated model API duration in milliseconds
- `TurnCount`, `ToolCallCount`, `FailedToolCallCount`, `SessionErrorCount` (INTEGER, nullable) — stage loop and tool execution counters
- `ReachedIdle`, `WasAborted` (INTEGER/boolean, nullable) — session completion state observed from SDK events
- `ProviderCallIds`, `ApiCallIds` (TEXT, nullable) — comma-separated request identifiers for provider/API correlation

Run details show total tokens, estimated cost, assistant turns, tool calls, failed tool calls, total model API time, and per-stage metric badges when data is available.

**`PipelineArtifacts` table** (added in migration `AddPipelineArtifacts`):
- `RunId` (TEXT, required) — owning pipeline run, cascade-deleted with the run
- `StageLogId` (INTEGER, nullable) — related stage log; set to null when the log is deleted
- `StageName` (TEXT, required) — stage that produced the artifact
- `Name` (TEXT, required) — artifact name or type from the structured stage result
- `Value` (TEXT, nullable) — artifact summary or value
- `Uri` (TEXT, nullable) — link to the generated artifact, PR, log, or external record
- `MediaType` (TEXT, nullable) — media type for linked or inline artifact content
- `ContractVersion` (TEXT, nullable) — structured result contract version that produced the artifact
- `Source` (TEXT, required) — capture source, currently `stage-result`
- `CreatedAt` (TEXT, required) — UTC capture timestamp

Middleware order:

```text
1. ExceptionHandler / HSTS in non-development
2. HTTPS redirection
3. Status code re-execution to /Home/Error
4. Routing
5. Authorization
6. Pipeline database migration
7. Static assets
8. Attribute-routed controllers
9. /health/ready health checks
10. /pipelineHub SignalR hub
11. Default MVC route
```

Controllers:

| Controller | Routes | Purpose |
|------------|--------|---------|
| `HomeController` | `/`, `/Home/Index`, `/Home/Error` | Pipeline portal and error view |
| `PipelinesController` | `/Pipelines`, `/Pipelines/Issues`, `/Pipelines/{id}`, `/Pipelines/{id}/Continue`, `/Pipelines/{id}/RetryStage`, `/Pipelines/{id}/ResetMission`, `/Pipelines/Guide/{mode}` | Pipeline modes, stages, run history, issue launcher, run details, run continuation, stage retry, replay reset, and Markdown guides |
| `PipelineAdminController` | `/PipelineAdmin`, `/PipelineAdmin/Pipelines/*`, `/PipelineAdmin/Policies/*` | Operator admin views for creating and editing custom JSON pipeline definitions, stages, transitions, gates, and policy profiles |
| ASP.NET health checks | `/health/ready` | Readiness check |

## Pipeline Assets

Local mode uses [.github/agents/cyberpilot.agent.md](.github/agents/cyberpilot.agent.md) as the controller agent. It delegates to specialist agents for triage, planning, implementation, review, docs, and delivery.

Cloud mode uses `.github/workflows/cloud-*.md` source files compiled to `.lock.yml` files with `gh aw compile`. Always delete cloud lockfiles before recompiling so the lock pins a current AWF binary.

SDK mode uses [copilot-sdk/Pipeline/SdkCyberpilotRunner.cs](copilot-sdk/Pipeline/SdkCyberpilotRunner.cs) to run stage prompts through Copilot SDK sessions. The command-line executable lives in [copilot-sdk-exe/](copilot-sdk-exe) and references the SDK library. It shares the same agent prompt files as local mode.

SDK stage prompts include a harness-owned context block generated by [copilot-sdk/Pipeline/PromptBuilder.cs](copilot-sdk/Pipeline/PromptBuilder.cs). [copilot-sdk/Pipeline/StageContextSnapshot.cs](copilot-sdk/Pipeline/StageContextSnapshot.cs) is the typed source for that block and for the `get_pipeline_context` deterministic tool, so prompt context and tool context do not drift. [copilot-sdk/Pipeline/PipelineExecutionContext.cs](copilot-sdk/Pipeline/PipelineExecutionContext.cs) records issue, repository, run ID, branch, base branch, pull request, definition, prior stage artifacts, evidence, and known approvals as stages complete. The prompt builder renders this as compact JSON under `## Harness Context` and prunes it by stage: triage gets minimal issue/repository context, plan and implement receive branch and relevant prior summaries, and review/docs/deliver receive pull-request-first context when known. Agents should treat this context and structured artifacts as canonical workflow state; issue and pull request comments remain human-readable reports. Runtime preferences let operators choose command guidance (`Auto`, `Windows`, or `Linux`) and system-message delivery (`none`, `append`, or `replace`) with `full` or `lean` harness profiles. Stage-specific system-message defaults can override the global mode/profile unless a CLI invocation explicitly passes `--system-message-mode` or `--system-message-profile`.

The SDK executable includes benchmark-oriented controls for prompt optimization. `--only-stage <stage>` runs one stage and stops, `--variant <name>` tags the persisted run, and `--seed-stage-result <stage>=<variant>` loads a completed stage result from `PipelineStageLogs.StageResultJson` into the new run's in-memory `StageHistory` before executing. This allows plan/review prompt variants to run against identical prior-stage input. PR-first review runs can pass `--pr-head-branch <branch>` and `--pr-number <number>` so routing preserves a known PR head branch and skips issue/branch rediscovery without assuming the issue number and PR number match.

SDK stage policy allows triage and plan to run investigative commands, searches, builds, tests, and scripts, but blocks durable side effects such as issue/PR comments, label edits, branch creation, file writes, commits, pushes, and direct API mutations. Those stages return intended comments, labels, branch names, and plans as structured artifacts; write-enabled stages or harness-owned code perform durable writes when appropriate. The mutation guard applies across shell and script wrappers so Python/Node subprocess calls cannot bypass the same policy.

Review and docs stages can also call deterministic tools for fresh PR metadata and diff summaries instead of searching issue comments. Tool outputs stay compact by default and include artifact references when detailed JSON has been captured for the run ledger.

SDK pipeline routing is definition-driven. Built-in definitions live under [copilot-sdk/Pipeline/](copilot-sdk/Pipeline) and include the full `cyberpilot-default` flow plus focused variants such as `bugfix` and `docs-only`. The runner can also load additional JSON definitions through `--pipeline-definition-file`; file-backed definitions are combined with built-ins and validated before issue, label, model, or stage side effects. The web admin surface writes operator-managed definitions to `web/App_Data/pipeline-definitions.json` by default, exposes them in the issue launcher, and passes that file path to queued SDK runs when present.

Web-triggered SDK runs can separate the controller repository from the target repository. `Cyberpilot:AgentPromptRoot` points at the repository containing [.github/agents/](.github/agents), while each configured repository's `RepoRoot` points at the clone where code changes happen. This allows one Cyberpilot installation to drive issue-to-PR work across repositories that do not contain Cyberpilot's agent files.

The web runner processes SDK runs through a durable database record plus an in-memory execution queue. Startup re-enqueues persisted `Queued` runs so a web app restart does not strand them. Runs for different configured repository roots can execute concurrently. Runs that target the same local repository root are serialized with a per-root lock, because simultaneous SDK runs can otherwise contend over the same checkout and branch state.

Run details include an operational Run Room with issue title/body context, a first-class Plan Review panel rendered from the latest `plan` stage's structured `StageResultJson`/evidence, live SignalR agent output, continuation for terminal runs, cancellation for active runs, and Reset Mission for replay testing. Reset Mission removes SDK stage labels while preserving the base `sdk` label, deletes recognizable Cyberpilot issue comments, deletes the SDK issue branch locally/remotely when present, and removes the local `PipelineRun` record with its cascaded `PipelineStageLog` rows. Reset Mission is intentionally unavailable once a run completes delivery, because the associated code has already been merged.

**Stage Retry & Selective Re-run:** Terminal runs (Failed, Stopped, Cancelled, Paused) expose a "⤹ Resume From Stage" panel in the Run Room. Any valid stage can be retried by name, with optional model and timeout overrides per attempt. Failed stage cards also surface a one-click Retry button. The `RetryStage` endpoint (`POST /Pipelines/{id}/RetryStage`) validates the stage name, enforces the `MaxStageRetries` cap (default: 3, configurable in `appsettings.json` under `Cyberpilot:MaxStageRetries`), and re-queues the run from the chosen stage. Remote runs (`IsRemote = true`) cannot use stage retry — the UI hides the controls and the server enforces the guard.

## Testing

| Project | Purpose |
|---------|---------|
| `Cyberpilot.Web.UnitTests` | Controller-level tests for `HomeController` |
| `Cyberpilot.Web.IntegrationTests` | Web smoke tests for `/` and `/health/ready` |
| `Cyberpilot.Web.PlaywrightTests` | Browser smoke tests in CI |
| `Cyberpilot.Sdk.Tests` | SDK option parsing, label handling, stage results, and runner behavior |

Common commands:

```bash
dotnet build
dotnet test
dotnet run --project web/Cyberpilot.Web.csproj
dotnet test tests/Cyberpilot.Web.UnitTests
dotnet test tests/Cyberpilot.Web.IntegrationTests
dotnet test tests/Cyberpilot.Sdk.Tests/Cyberpilot.Sdk.Tests.csproj
```

## Documentation Ownership

Keep this file updated when web project structure, middleware, controllers, dependencies, or test projects change. Keep pipeline behavior documented in the canonical AI-SDLC guide:

- [AI-SDLC.md](AI-SDLC.md)
