# Cyberpilot

Cyberpilot is a pipeline-first AI-SDLC repository. The MVC web app now lives under [web/](web) and exists as the portal for local, cloud, and SDK pipeline activity.

## Pipeline Modes

| Mode | Entry Point | State Labels | Guide |
|------|-------------|--------------|-------|
| Local | VS Code Copilot Chat or Copilot CLI | `local`, `local/*` | [AI-SDLC.md#local-mode](AI-SDLC.md#local-mode) |
| Cloud | GitHub Agentic Workflows | `cyberpilot`, `cloud/*` | [AI-SDLC.md#cloud-mode](AI-SDLC.md#cloud-mode) |
| SDK | .NET console app in [copilot-sdk-exe/](copilot-sdk-exe) | `sdk`, `sdk/*` | [AI-SDLC.md#sdk-mode](AI-SDLC.md#sdk-mode) |

All modes follow the same delivery shape:

```text
Issue -> Triage -> Plan -> Implement -> Review -> Docs -> Deliver
```

The GitHub issue is the state file. Each stage writes structured comments so downstream stages can continue with a clear handoff.

## What Remains In The Web App

The ASP.NET Core MVC app is now a focused support surface for the pipeline:

- [web/Controllers/HomeController.cs](web/Controllers/HomeController.cs) renders the pipeline portal.
- [web/Controllers/PipelinesController.cs](web/Controllers/PipelinesController.cs) renders pipeline modes, stages, run history, issue launcher, run details, continuation, replay reset, and Markdown guides.
- ASP.NET health checks expose readiness at `/health/ready`.
- [web/Views/Home/Index.cshtml](web/Views/Home/Index.cshtml) links the canonical pipeline guide.

Removed demo-era concerns include achievement persistence, component showcase pages, security lab routes, weather APIs, demo admin UI, Swagger, Application Insights wiring, Redis/session state, and custom middleware.

## Repository Map

| Path | Purpose |
|------|---------|
| [.github/agents/](.github/agents) | Local custom agents shared by local and SDK modes; web SDK runs can load these from `AgentPromptRoot` while editing another target repo |
| [.github/skills/](.github/skills) | Reusable skills such as gh-aw compile and commit/PR workflow helpers |
| [.github/workflows/](.github/workflows) | GitHub Actions and gh-aw workflow sources/locks |
| [architecture.md](architecture.md) | High-level solution structure, web pipeline, and component relationships |
| [copilot-sdk/](copilot-sdk) | Programmatic Cyberpilot SDK library and shared run-history persistence |
| [copilot-sdk-exe/](copilot-sdk-exe) | Console harness that references the SDK library |
| [web/](web) | ASP.NET Core MVC pipeline portal, run launcher, and SignalR dashboard over SDK persistence |
| [tests/Cyberpilot.Sdk.Tests/](tests/Cyberpilot.Sdk.Tests) | SDK runner tests |
| [tests/Cyberpilot.Web.UnitTests/](tests/Cyberpilot.Web.UnitTests) | Focused MVC controller tests |
| [tests/Cyberpilot.Web.IntegrationTests/](tests/Cyberpilot.Web.IntegrationTests) | Web smoke tests through `WebApplicationFactory` |
| [docs/](docs) | Pipeline and operational documentation |

## Running

Prerequisites:

- .NET 10 SDK or later
- GitHub CLI for local and SDK pipeline operations
- `gh aw` when compiling GitHub Agentic Workflow sources

### Cloud Secrets

Cloud pipeline workflows require two repository secrets in `MSBart2/Cyberpilot`:

- `COPILOT_GITHUB_TOKEN`
	- Token type: Fine-grained personal access token (PAT)
	- Resource owner: your personal user account (not an organization)
	- Required permission: Account permissions -> Copilot Requests: Read
	- Purpose: Authenticates the Copilot engine used by gh-aw workflows
- `CROSS_REPO_PAT`
	- Token type: Fine-grained personal access token (PAT)
	- Resource owner: `MSBart2` (or the owner of the target repositories)
	- Repository access: include `MSBart2/Cyberpilot` and each target repository (for example `MSBart2/Aspire1`)
	- Required repository permissions:
		- Contents: Read and write
		- Issues: Read and write
		- Pull requests: Read and write
	- Purpose: Enables cross-repo checkout, issue updates, agent assignment, and pull-request operations in target repos

Set/update secrets with GitHub CLI:

```bash
gh secret set COPILOT_GITHUB_TOKEN --repo MSBart2/Cyberpilot
gh secret set CROSS_REPO_PAT --repo MSBart2/Cyberpilot
```

Build and run:

```bash
dotnet build
dotnet run --project web/Cyberpilot.Web.csproj
```

Local launch settings bind the app to `http://localhost:5203` by default. The web app exposes the pipeline portal at `/Pipelines` and the issue launcher at `/Pipelines/Issues`. The Run Room for a web-triggered SDK run shows the GitHub issue title/body, live agent output, stage telemetry, and operational controls:

- **Continue Run** requeues a terminal run using the same run record.
- **Cancel Run** marks an active queued/running run as cancelled.
- **Reset Mission** prepares an issue for replay by clearing SDK stage labels, deleting recognizable Cyberpilot comments, deleting the SDK issue branch when present, and removing the local run/stage-log records. It is hidden and rejected after a delivered run, because the code has already been merged.

The web runner can process missions for different configured repository roots at the same time. Missions that use the same local checkout stay serialized so two agents do not fight over one working tree or branch.

SDK mode:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- issue 135 --repo rbmathis/Cyberpilot --approve-all --skip-deliver
```

Add `--db "Data Source=<path>"` to persist EXE runs into the SDK-owned run-history database.

Useful SDK preflights:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-model --repo rbmathis/Cyberpilot
```

## Testing

```bash
dotnet test
dotnet test tests/Cyberpilot.Web.UnitTests
dotnet test tests/Cyberpilot.Web.IntegrationTests
dotnet test tests/Cyberpilot.Sdk.Tests/Cyberpilot.Sdk.Tests.csproj
```

Playwright smoke tests are kept for CI and skip locally unless CI environment variables are present.

## Workflow Maintenance

When editing `.github/workflows/cloud-*.md`, delete the existing lockfiles before compiling:

```powershell
Remove-Item .github/workflows/cloud-*.lock.yml -ErrorAction SilentlyContinue
gh aw compile
```

Commit regenerated `.lock.yml` files with their `.md` sources. Do not edit lockfiles by hand.

## Start Here

- [AI-SDLC.md](AI-SDLC.md) for local, cloud, and SDK AI-SDLC modes.
- [architecture.md](architecture.md) for the current repository and web app architecture overview.
