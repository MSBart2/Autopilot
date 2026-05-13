# Web Integration Test Instructions

These instructions apply to the web integration test project.

## Scope

- Use this project for ASP.NET Core startup, routing, middleware, health endpoint, and smoke-test behavior.
- Prefer `WebApplicationFactory`-style tests over manual server bootstrapping.
- Keep integration tests independent of developer machine state and external network services.
- Cover important route and service-registration changes here when unit tests cannot see the behavior.

## Validation

- From the repository root, run `dotnet test .\tests\Cyberpilot.Web.IntegrationTests\Cyberpilot.Web.IntegrationTests.csproj` for this project.
- After a successful solution build, use `dotnet test .\tests\Cyberpilot.Web.IntegrationTests\Cyberpilot.Web.IntegrationTests.csproj --no-build`.