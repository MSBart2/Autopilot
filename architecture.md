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
| `ModelPricingService` | Static class in `Cyberpilot.Persistence`; maps model IDs to per-1M-token USD rates and returns `0` for unknown models. Powers cost estimation in both sink implementations. |

**Token capture flow:** `CopilotStageRunner` calls `session.Rpc.Usage.GetMetricsAsync()` after each `SendAndWaitAsync`, stamps `InputTokens` and `OutputTokens` onto the returned `StageResult` (non-fatal — wrapped in try/catch), and both sink implementations (`CyberpilotRunHistoryProgressSink` and `SignalRProgressSink`) write those values plus `EstimatedCostUsd` (via `ModelPricingService.Estimate`) and `RetryCount` to `PipelineStageLog`.

**`PipelineStageLog` columns:**

Token tracking (added in migration `AddTokenUsageToPipelineStageLog`):
- `InputTokens` (INTEGER, nullable) — input tokens for this stage call
- `OutputTokens` (INTEGER, nullable) — output tokens for this stage call
- `EstimatedCostUsd` (REAL, nullable) — computed cost using `ModelPricingService`

Retry tracking (added in migration `AddRetryCountToPipelineStageLog`):
- `RetryCount` (INTEGER, nullable) — attempt index for this stage (0 = first attempt, 1 = first retry, etc.)

The `RetryCount` value is set by both sink implementations (`SignalRProgressSink` and `CyberpilotRunHistoryProgressSink`) by counting existing logs for the same run and stage before inserting the new row.

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
| ASP.NET health checks | `/health/ready` | Readiness check |

## Pipeline Assets

Local mode uses [.github/agents/cyberpilot.agent.md](.github/agents/cyberpilot.agent.md) as the controller agent. It delegates to specialist agents for triage, planning, implementation, review, docs, and delivery.

Cloud mode uses `.github/workflows/cloud-*.md` source files compiled to `.lock.yml` files with `gh aw compile`. Always delete cloud lockfiles before recompiling so the lock pins a current AWF binary.

SDK mode uses [copilot-sdk/Pipeline/SdkCyberpilotRunner.cs](copilot-sdk/Pipeline/SdkCyberpilotRunner.cs) to run stage prompts through Copilot SDK sessions. The command-line executable lives in [copilot-sdk-exe/](copilot-sdk-exe) and references the SDK library. It shares the same agent prompt files as local mode.

Web-triggered SDK runs can separate the controller repository from the target repository. `Cyberpilot:AgentPromptRoot` points at the repository containing [.github/agents/](.github/agents), while each configured repository's `RepoRoot` points at the clone where code changes happen. This allows one Cyberpilot installation to drive issue-to-PR work across repositories that do not contain Cyberpilot's agent files.

The web runner processes SDK runs through a durable database record plus an in-memory execution queue. Startup re-enqueues persisted `Queued` runs so a web app restart does not strand them. Runs for different configured repository roots can execute concurrently. Runs that target the same local repository root are serialized with a per-root lock, because simultaneous SDK runs can otherwise contend over the same checkout and branch state.

Run details include an operational Run Room with issue title/body context, live SignalR agent output, continuation for terminal runs, cancellation for active runs, and Reset Mission for replay testing. Reset Mission removes SDK stage labels while preserving the base `sdk` label, deletes recognizable Cyberpilot issue comments, deletes the SDK issue branch locally/remotely when present, and removes the local `PipelineRun` record with its cascaded `PipelineStageLog` rows. Reset Mission is intentionally unavailable once a run completes delivery, because the associated code has already been merged.

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
