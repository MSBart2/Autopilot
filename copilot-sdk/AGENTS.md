# SDK Library Instructions

These instructions apply when working in the Cyberpilot SDK library.

## Ownership

- This project owns Cyberpilot's programmatic Copilot SDK harness: stage orchestration, prompt loading, session execution, permission policy, Git/GitHub integration helpers, options, progress reporting, and shared SQLite persistence.
- Keep reusable pipeline behavior here rather than in the web app or console harness.
- The SDK is consumed by both [`../web/Cyberpilot.Web.csproj`](../web/Cyberpilot.Web.csproj) and [`../copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj`](../copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj).
- Persistence schema changes belong under `Persistence/Migrations/` and must stay compatible with web-triggered runs.

## Copilot SDK Reference

- Use https://github.com/github/copilot-sdk as the upstream source for SDK behavior, API names, examples, and public-preview changes.
- For .NET API lookups, start with https://github.com/github/copilot-sdk/tree/main/dotnet and its `README.md` before guessing names or shapes.
- For broader feature behavior, check the upstream docs for getting started, authentication, features, troubleshooting, and compatibility under https://github.com/github/copilot-sdk/tree/main/docs.
- Remember that the upstream SDK is in public preview; verify current APIs before making non-trivial changes around sessions, permissions, tools, hooks, or model/provider configuration.

## SDK Harness Capabilities

- The Copilot SDK exposes the Copilot agent runtime programmatically. Cyberpilot can build its own harness around it rather than relying only on the interactive CLI or VS Code agent mode.
- The harness can create and resume sessions, select models, send prompts, stream or collect events, attach files/images, manage session lifetime, and capture assistant/tool/error events.
- The harness can define the execution contract: system message customization, stage prompts, custom agents, skills, MCP servers, custom tools, slash commands, user-input handlers, hooks, and permission handlers.
- The SDK manages communication with the Copilot CLI server over JSON-RPC. For .NET usage, the CLI is bundled by default, but the client can also connect to an external CLI server when needed.
- Permission handling is part of the product surface. Prefer explicit, auditable permission policy over blanket approval; keep `PermissionHandler.ApproveAll` behind deliberate user flags such as `--approve-all`.
- Treat the SDK as the agent engine, not the whole application. Cyberpilot remains responsible for pipeline state, issue labels, branch policy, persistence, progress output, failure handling, and delivery gates.

## Implementation Rules

- Keep public APIs documented and stable where practical; this project generates XML documentation.
- Prefer explicit request/result types over loosely typed dictionaries or stringly typed stage state.
- Keep GitHub CLI, repository parsing, branch provisioning, and Copilot SDK concerns behind focused abstractions.
- Preserve cancellation, progress reporting, and stage log behavior across runner changes.
- Treat external command execution, GitHub API calls, and repository paths as failure-prone. Validate inputs and surface actionable errors.
- Keep Copilot sessions bounded by clear stage contracts. Parse structured results defensively and fail closed when required result data is missing or malformed.
- When adding SDK features, decide whether they belong in the reusable library, the console composition root, or the web queue/background-service layer before wiring them in.

## Validation

- Prefer `dotnet build ..\Cyberpilot.sln` before running tests.
- For SDK behavior, run `dotnet test ..\tests\Cyberpilot.Sdk.Tests\Cyberpilot.Sdk.Tests.csproj --no-build` after a successful build.
- Broaden to `dotnet test ..\Cyberpilot.sln --no-build` when changes affect persistence, shared options, or behavior consumed by the web app.