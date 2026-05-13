# Testing

Cyberpilot keeps web tests small and focused. Pipeline behavior is primarily covered by SDK tests and workflow/agent review.

## Test Projects

| Project | Purpose |
|---------|---------|
| `tests/Cyberpilot.Web.UnitTests` | Unit tests for the minimal MVC controllers |
| `tests/Cyberpilot.Web.IntegrationTests` | `WebApplicationFactory` smoke tests for the portal and readiness endpoint |
| `tests/Cyberpilot.Web.PlaywrightTests` | Browser smoke tests, skipped locally unless CI variables are present |
| `tests/Cyberpilot.Sdk.Tests` | SDK runner behavior, options, labels, parsing, and stage results |

## Commands

Run everything:

```bash
dotnet test
```

Run focused suites:

```bash
dotnet test tests/Cyberpilot.Web.UnitTests
dotnet test tests/Cyberpilot.Web.IntegrationTests
dotnet test tests/Cyberpilot.Sdk.Tests/Cyberpilot.Sdk.Tests.csproj
```

Run the canonical controller coverage gate:

```bash
dotnet test tests/Cyberpilot.Web.UnitTests/Cyberpilot.Web.UnitTests.csproj \
  --configuration Release \
  --nologo \
  --verbosity normal \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  "/p:Include=[Cyberpilot.Web]Cyberpilot.Web.Controllers.*" \
  /p:Threshold=35 \
  /p:ThresholdType=line \
  /p:ThresholdStat=total
```

Build without tests:

```bash
dotnet build
```

## Playwright

Playwright tests live in `tests/Cyberpilot.Web.PlaywrightTests`. They are retained as CI smoke coverage and intentionally skip on local machines unless `CI` or `GITHUB_ACTIONS` is set.
