# Test Project Instructions

These instructions apply when working under the test projects.

## Shared Testing Rules

- Keep tests focused on observable behavior instead of private implementation details.
- Prefer the smallest meaningful test project for validation before running the full suite.
- Use clear test names that describe the behavior and expected outcome.
- Avoid tests that depend on real GitHub network access, developer-specific paths, wall-clock timing, or test execution order.
- When production behavior changes, update or add tests in the project that owns the nearest behavior surface.

## Test Projects

- `Cyberpilot.Web.UnitTests` uses xUnit for controller-level tests.
- `Cyberpilot.Web.IntegrationTests` uses xUnit and ASP.NET Core test hosting for web smoke and endpoint behavior.
- `Cyberpilot.Web.PlaywrightTests` uses NUnit plus Playwright for browser smoke coverage.
- `Cyberpilot.Sdk.Tests` uses xUnit for SDK options, GitHub helpers, stage results, and runner behavior.

## Validation

- Build first with `dotnet build ..\Cyberpilot.sln` when production code changed.
- Run a single test project with `dotnet test .\<ProjectName>\<ProjectName>.csproj --no-build` from this folder after a successful build.
- Use `dotnet test ..\Cyberpilot.sln --no-build` when shared SDK, persistence, or web startup behavior changes.