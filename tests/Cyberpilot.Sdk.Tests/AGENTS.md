# SDK Test Instructions

These instructions apply to the SDK test project.

## Scope

- Use this project for SDK runner behavior, options, GitHub helpers, label handling, stage results, persistence-facing logic, and console harness behavior exposed through internals.
- Prefer deterministic fakes or local fixtures over real GitHub CLI, network, or repository side effects.
- Assert progress, cancellation, error handling, and stage result behavior when changing runner flow.
- Keep tests close to the SDK abstraction being exercised rather than driving through the web app unless the behavior is web-specific.

## Validation

- From the repository root, run `dotnet test .\tests\Cyberpilot.Sdk.Tests\Cyberpilot.Sdk.Tests.csproj` for this project.
- After a successful solution build, use `dotnet test .\tests\Cyberpilot.Sdk.Tests\Cyberpilot.Sdk.Tests.csproj --no-build`.