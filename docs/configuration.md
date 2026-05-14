# Configuration

Cyberpilot's web project lives under [web/](../web) and keeps configuration focused on the pipeline portal and SDK runner.

## Web App

[web/appsettings.json](../web/appsettings.json) contains logging, host settings, SQLite run-log storage, and web runner options:

```json
{
  "ConnectionStrings": {
    "CyberpilotDb": "Data Source=cyberpilot.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Cyberpilot": {
    "Repository": "rbmathis/Cyberpilot",
    "Repositories": [
      {
        "Name": "Cyberpilot",
        "Repository": "rbmathis/Cyberpilot",
        "RepoRoot": "..",
        "Token": ""
      }
    ],
    "RepoRoot": "..",
    "AgentPromptRoot": "..",
    "ApproveAll": false,
    "EnsureLabels": true,
    "MaxStageRetries": 3
  }
}
```

[web/appsettings.Development.json](../web/appsettings.Development.json) enables `Cyberpilot:ApproveAll` for local web-triggered SDK runs. `Cyberpilot:EnsureLabels` defaults to `true`, so web-triggered SDK runs create missing `sdk/*` labels in newly configured repositories before triage starts. `Cyberpilot:MaxStageRetries` defaults to `3` — once a stage has been attempted this many times on a single run, the Retry button is hidden and the `RetryStage` endpoint blocks further attempts. `GitHub:Token` is intentionally blank; set `GITHUB_TOKEN`, `GH_TOKEN`, or a user-secret value instead of committing a token. Each `Cyberpilot:Repositories` entry can provide its own `RepoRoot`; the web runner clones the configured repository there when the path is missing, validates the local git work tree, and then lets the SDK create or switch issue branches in that clone. `Cyberpilot:AgentPromptRoot` points to the controller repository that contains `.github/agents`; when omitted, the web app uses the parent of the web content root.

### Controller Repository and Target Repositories

The web runner supports a controller-repository pattern for multi-repo automation. The Cyberpilot repository remains the controller because it owns `.github/agents`, SDK orchestration code, and shared AI-SDLC policy. Each configured repository is a target because it owns the issue, branch, code changes, tests, and pull request.

Use `AgentPromptRoot` for the controller repository and per-entry `RepoRoot` values for target clones:

```json
"Cyberpilot": {
  "AgentPromptRoot": "C:\\Users\\rdpuser\\Source\\Cyberpilot",
  "Repositories": [
    {
      "Name": "Nonograms",
      "Repository": "MSBart2/Nonograms",
      "RepoRoot": "C:\\Users\\rdpuser\\Source\\Nonograms",
      "Token": ""
    }
  ]
}
```

When `RepoRoot` is missing, the web runner clones `Repository` into that path before SDK execution. When the SDK builds a stage prompt, it reads from `AgentPromptRoot/.github/agents` and passes the target `RepoRoot` to Copilot as the workspace to inspect and edit. This lets one Cyberpilot controller drive repositories that do not contain Cyberpilot's agent files.

The web app does not configure Redis, external configuration stores, Application Insights, Swagger, admin credentials, or session state.

## Database Migrations

The web app stores dashboard-launched SDK runs in SQLite through `ConnectionStrings:CyberpilotDb`. The shared SDK persistence layer owns the EF Core context and migrations in [copilot-sdk/Persistence/](../copilot-sdk/Persistence), and web startup applies pending migrations with `Database.MigrateAsync()`.

Create a migration after changing persisted models or [copilot-sdk/Persistence/CyberpilotDbContext.cs](../copilot-sdk/Persistence/CyberpilotDbContext.cs):

```powershell
dotnet ef migrations add <MigrationName> --project .\copilot-sdk\Cyberpilot.Sdk.csproj --startup-project .\web\Cyberpilot.Web.csproj --output-dir Persistence\Migrations
```

Apply migrations manually when needed:

```powershell
dotnet ef database update --project .\copilot-sdk\Cyberpilot.Sdk.csproj --startup-project .\web\Cyberpilot.Web.csproj
```

The first migration is `InitialCyberpilotSchema`, which creates `PipelineRuns` and `PipelineStageLogs`. Startup includes a one-time compatibility path for local SQLite files that were created with `EnsureCreated` before migrations were introduced.

## SDK Runner

The SDK runner accepts configuration through command-line options:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- issue 135 --repo rbmathis/Cyberpilot --approve-all --skip-deliver
```

Important options:

| Option | Purpose |
|--------|---------|
| `--repo-root <path>` | Repository root. Defaults to the nearest parent containing `.github/agents`. |
| `--repo <owner/name>` | GitHub repository for issue and PR operations. |
| `--model <model-id>` | Copilot model. Defaults to the SDK runner default. |
| `--stage-timeout-minutes <minutes>` | Per-stage timeout. |
| `--pipeline-definition <name>` | Pipeline definition to run. Built-ins include `cyberpilot-default`, `bugfix`, and `docs-only`. |
| `--pipeline-definition-file <path>` | Load additional JSON pipeline definitions from a file. |
| `--pipeline-version <version>` | Pipeline definition version. Defaults to `1.0`. |
| `--policy-profile <name>` | Policy profile to apply: `lenient`, `standard`, `strict`, or `security-critical`. |
| `--approve-all` | Allow Copilot SDK tool permission requests. |
| `--db <connection>` | Persist an EXE-triggered run to the shared SDK run-history database. |
| `--skip-deliver` | Stop before merge/deliver. Useful for pilots. |

Preflights:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --ensure-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-model --repo rbmathis/Cyberpilot
```

### Pipeline Definitions

The SDK runner defaults to `cyberpilot-default`, which runs the full `triage -> plan -> implement -> review -> docs -> deliver` flow. The built-in `bugfix` definition skips triage and docs for focused fixes, and `docs-only` runs only documentation plus delivery.

Select a built-in definition from the command line:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --pipeline-definition bugfix
```

Use `--pipeline-definition-file` when experimenting with additional definitions without recompiling the SDK. File definitions are loaded alongside built-ins, and file definitions take precedence when names overlap. Invalid or missing definition files stop the run before issue labels, model checks, or stage execution.

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --pipeline-definition custom-docs --pipeline-definition-file .\pipelines\custom.json
```

Definition files use JSON with a top-level `definitions` array:

```json
{
  "definitions": [
    {
      "name": "custom-docs",
      "version": "1.0",
      "policyProfile": {
        "name": "standard",
        "strictness": "standard"
      },
      "stages": [
        {
          "displayName": "DOCS",
          "name": "docs",
          "promptFile": "docs.agent.md",
          "label": "sdk/docs",
          "contract": {
            "version": "1.0",
            "requiredArtifacts": ["documentation-summary"]
          }
        },
        {
          "displayName": "LAND",
          "name": "deliver",
          "promptFile": "deliver.agent.md",
          "label": "sdk/delivering",
          "contract": {
            "version": "1.0",
            "requiredArtifacts": ["landing-report"]
          }
        }
      ],
      "transitions": [
        {
          "fromStage": "docs",
          "toStage": "deliver",
          "condition": "GO"
        }
      ]
    }
  ]
}
```

Each stage still uses the controller repository's `.github/agents/<promptFile>` prompt. A file-backed definition can reorder or omit stages, but it should use stage names and transitions that the SDK engine understands. Every selected definition is validated before routing starts.
