# Cyberpilot SDK EXE

This project is the console harness for the Cyberpilot SDK library in [../copilot-sdk/](../copilot-sdk). It contains the executable entrypoint and references the SDK project for the actual runner implementation.

## Run

From the repository root:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --skip-deliver
```

Persist a CLI run into a repo-specific Cyberpilot database:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --db "Data Source=.cyberpilot\rbmathis-Cyberpilot.sdk.db" --approve-all --skip-deliver
```

Useful preflights:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-labels --repo rbmathis/Cyberpilot
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- --check-model --repo rbmathis/Cyberpilot
```

Select a built-in process definition or policy profile when the default full SDLC is too broad:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --pipeline-definition bugfix --policy-profile strict
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --pipeline-definition docs-only --skip-deliver
```

Load additional JSON-backed definitions with `--pipeline-definition-file`:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --pipeline-definition custom-docs --pipeline-definition-file .\pipelines\custom.json
```

## Repository Tokens From Config

The EXE can use the same `Cyberpilot:Repositories` shape as the web app. By default it looks for `appsettings.json` and `appsettings.Development.json` in the current directory and under `web/` in the repo root. You can also pass an explicit config file:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --config .\web\appsettings.json --approve-all --skip-deliver
```

Example configuration:

```json
"Cyberpilot": {
	"Repository": "rbmathis/Cyberpilot",
	"Repositories": [
		{
			"Name": "Cyberpilot",
			"Repository": "rbmathis/Cyberpilot",
			"RepoRoot": "..",
			"Token": ""
		}
	]
}
```

Keep real tokens out of committed JSON. Environment variables can fill the same indexed configuration values:

```powershell
$env:Cyberpilot__Repositories__0__Repository = "rbmathis/Cyberpilot"
$env:Cyberpilot__Repositories__0__RepoRoot = "C:\Users\rdpuser\Source\Cyberpilot"
$env:Cyberpilot__Repositories__0__Token = "github_pat_..."
```

If `--repo` is omitted, the EXE uses `Cyberpilot:Repository` or the first configured repository. If a matching repository has `RepoRoot`, SDK execution runs in that local clone. If no repo-specific token is configured, it falls back to `GITHUB_TOKEN` or `GH_TOKEN`.
