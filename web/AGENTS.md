# Web Project Instructions

These instructions apply when working in the ASP.NET Core MVC web project.

## Ownership

- The web project is a small pipeline portal, not a general demo app or admin surface.
- Keep controllers thin and put pipeline orchestration in services or the SDK.
- Use [`../architecture.md`](../architecture.md) as the source of truth for routes, middleware order, registered services, and web behavior.
- The web app references [`../copilot-sdk/Cyberpilot.Sdk.csproj`](../copilot-sdk/Cyberpilot.Sdk.csproj); shared run history and migrations live in the SDK project.

## Implementation Rules

- Preserve the existing MVC boundaries: controllers handle HTTP flow, models validate inputs, services own business logic, and views stay simple.
- Keep pipeline routes, SignalR updates, health checks, and database migration behavior consistent with [`Program.cs`](Program.cs).
- Avoid adding session state, custom middleware, Swagger, telemetry packages, Redis, feature-admin UI, or demo APIs unless the architecture changes explicitly require it.
- Use dependency injection for services and options. Do not reach into static service locators.
- Keep user-facing errors useful but avoid leaking repository paths, tokens, raw command output, or internal exception details.

## Validation

- For narrow web changes, prefer `dotnet build ..\Cyberpilot.sln`.
- For controller changes, run `dotnet test ..\tests\Cyberpilot.Web.UnitTests\Cyberpilot.Web.UnitTests.csproj --no-build` after a successful build.
- For routing, health, startup, or middleware changes, run `dotnet test ..\tests\Cyberpilot.Web.IntegrationTests\Cyberpilot.Web.IntegrationTests.csproj --no-build`.
- For UI behavior that depends on browser rendering, run the Playwright tests when practical.

## More Specific Rules

- Follow `.github/instructions/controllers.instructions.md` for `Controllers/**`.
- Follow `.github/instructions/models.instructions.md` for `Models/**`.
- Follow `.github/instructions/views.instructions.md` for `Views/**`.