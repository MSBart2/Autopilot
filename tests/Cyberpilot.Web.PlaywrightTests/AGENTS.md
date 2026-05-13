# Web Playwright Test Instructions

These instructions apply to the browser smoke test project.

## Scope

- Use this project for browser-visible behavior that unit or integration tests cannot cover well.
- This project uses NUnit with `Microsoft.Playwright.NUnit`; do not mix in xUnit patterns here.
- Keep browser tests smoke-focused, resilient, and independent of test order.
- Prefer stable selectors and user-visible assertions over brittle DOM structure checks.

## Validation

- From the repository root, run `dotnet test .\tests\Cyberpilot.Web.PlaywrightTests\Cyberpilot.Web.PlaywrightTests.csproj` when Playwright coverage is relevant.
- If browser dependencies are missing, report that clearly and run the nearest non-browser test project when useful.