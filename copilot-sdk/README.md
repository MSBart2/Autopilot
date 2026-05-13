# Cyberpilot SDK Library

This project is the programmatic Copilot SDK library for the SDK mode described in [../AI-SDLC.md](../AI-SDLC.md#sdk-mode).

The executable harness lives in [../copilot-sdk-exe/](../copilot-sdk-exe) and references this project. The SDK library duplicates the local cyberpilot controller behavior in code, with SDK-specific safety gates:

- runs `triage -> plan -> implement -> review -> docs -> deliver`
- owns `sdk/*` stage label transitions deterministically through `gh`
- applies the plain `sdk` provenance label when work starts and never removes it
- reads stage prompts from the configured Cyberpilot controller repo's `.github/agents/*.agent.md`
- uses the GitHub issue thread as the pipeline state file
- supports the review rework loop with a maximum of two review cycles
- checks Copilot model availability before applying issue labels or running stages
- requires explicit `--approve-all` before granting Copilot SDK tool permissions
- exits before label changes or stage work when the target issue is already closed
- supports `--skip-deliver` for pilot runs that stop before merge

## Prerequisites

- .NET 10 SDK
- GitHub CLI authenticated with access to `rbmathis/Cyberpilot`
- GitHub Copilot access for the Copilot SDK runtime
- Push access to create branches and PRs when running the implement stage

## Run

From the repository root:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --skip-deliver
```

Use `--db "Data Source=<path>"` to persist EXE-triggered runs into the SDK-owned EF Core run-history database.

The default SDK model is `claude-sonnet-4.6`. Use `--model` when you need a different Copilot model available to your account.

Useful options:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --ensure-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-model --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-model --model claude-sonnet-4.6 --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --skip-deliver
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --allow-missing-docs
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --model claude-sonnet-4.6
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --stage-timeout-minutes 20
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo-root C:\Users\rdpuser\Source\Cyberpilot
```

Each Copilot stage waits up to 10 minutes by default (20 minutes when launched from the web UI). Use `--stage-timeout-minutes` for longer triage, implementation, review, or documentation runs.

## Required Labels

The runner fails before touching an issue unless every SDK label exists. Create or verify them explicitly:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --ensure-labels --repo rbmathis/Cyberpilot
```

Required labels:

- `sdk`
- `sdk/triage`
- `sdk/planning`
- `sdk/implementing`
- `sdk/review`
- `sdk/docs`
- `sdk/delivering`
- `sdk/done`
- `sdk/failed`

## How It Works

The console app uses the Copilot SDK to create a fresh Copilot session for each pipeline stage. Before each session starts, the app updates issue labels itself:

When a run targets a closed issue, the runner prints a no-op message and exits before it creates, removes, or adds issue labels.

Before the first mutable issue operation, the runner checks that the selected Copilot model is available. Use `--check-model` to run that preflight independently.

The console output groups preflights and stages into bordered sections, prints `[step]`, `[ ok ]`, `[warn]`, and `[fail]` status lines, and shows the timeout applied to each stage.

The plain `sdk` label is permanent provenance. The runner applies it once when work starts and never removes it. Stage transitions remove only labels with the `sdk/` prefix, then add the current stage label. Stage agents are instructed not to manage `sdk` or `sdk/*` labels.

| Stage | Label |
| ----- | ----- |
| Triage | `sdk/triage` |
| Plan | `sdk/planning` |
| Implement | `sdk/implementing` |
| Review | `sdk/review` |
| Docs | `sdk/docs` |
| Deliver | `sdk/delivering` |
| Complete | `sdk/done` |
| Failed | `sdk/failed` |

Each stage prompt is loaded from the existing local agent file, then wrapped with SDK controller instructions that require a final fenced JSON result block. The runner parses the last JSON block only and fails closed when the block is missing, malformed, or contains an unknown `status` or `decision`.

### Controller Prompts and Target Repositories

The SDK separates prompt source from execution target for programmatic and web-triggered runs. `RepoRoot` is the target repository where Copilot inspects files, creates branches, edits code, runs tests, commits, and opens pull requests. `AgentPromptRoot` is the controller repository that contains `.github/agents/*.agent.md`.

This is useful when a single Cyberpilot installation drives many repositories. For example, a web run can target `MSBart2/Nonograms` with `RepoRoot` set to `C:\Users\rdpuser\Source\Nonograms`, while `AgentPromptRoot` remains `C:\Users\rdpuser\Source\Cyberpilot`. The target repository does not need to contain `.github/agents`; it only needs to be cloneable and runnable as its own project.

Docs are blocking by default so human verification steps are recorded before delivery. Use `--allow-missing-docs` only when deliberately accepting that risk.

## Project Layout

- `../copilot-sdk-exe/Program.cs` wires and starts the app.
- `../copilot-sdk-exe/CyberpilotApp.cs` is the executable composition root.
- `Options/` handles CLI options and repository-root discovery.
- `GitHub/` wraps `gh` commands and owns SDK label behavior.
- `Copilot/` wraps Copilot SDK stage execution and model availability checks.
- `Pipeline/` owns stage definitions, prompt building, result parsing, and orchestration.
- `../tests/Cyberpilot.Sdk.Tests/` covers option parsing, label parsing, stage result parsing, label transitions, closed-issue no-op behavior, and model preflight behavior.

## Notes

- This is an SDK experiment, not a replacement for the VS Code local agents yet.
- The SDK package is in public preview, so APIs may move.
- The SDK runs with `PermissionHandler.ApproveAll` only when `--approve-all` is supplied; use this only in trusted local development environments.
- The SDK project is referenced by the MVC app at `../web/Cyberpilot.Web.csproj` for web-triggered runs.
