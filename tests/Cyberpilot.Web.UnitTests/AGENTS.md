# Web Unit Test Instructions

These instructions apply to the web unit test project.

## Scope

- Use this project for focused controller and MVC behavior tests that do not need a full browser or external services.
- Keep tests fast and deterministic. Mock collaborators with Moq when a controller depends on services.
- Prefer asserting returned `IActionResult` shape, model values, redirects, status codes, and validation behavior.

## Validation

- From the repository root, run `dotnet test .\tests\Cyberpilot.Web.UnitTests\Cyberpilot.Web.UnitTests.csproj` for this project.
- After a successful solution build, use `dotnet test .\tests\Cyberpilot.Web.UnitTests\Cyberpilot.Web.UnitTests.csproj --no-build`.