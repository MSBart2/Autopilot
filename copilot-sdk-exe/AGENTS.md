# SDK Executable Instructions

These instructions apply when working in the Cyberpilot SDK console harness.

## Ownership

- This project is a thin executable wrapper around the SDK library.
- Keep command-line parsing, configuration loading, and console output here; keep pipeline behavior in [`../copilot-sdk/`](../copilot-sdk/).
- The executable shares the `Cyberpilot` root namespace with the SDK and has internal visibility for SDK tests.

## Implementation Rules

- Do not duplicate SDK orchestration logic in the executable.
- Keep startup and configuration errors clear enough for local automation and CI logs.
- Avoid interactive prompts unless the command is explicitly designed for interactive use.
- Prefer deterministic exit codes and concise console output for automation.

## Validation

- Prefer `dotnet build ..\Cyberpilot.sln` for compile checks.
- For executable behavior covered through SDK tests, run `dotnet test ..\tests\Cyberpilot.Sdk.Tests\Cyberpilot.Sdk.Tests.csproj --no-build` after a successful build.