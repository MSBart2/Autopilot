# ✍️ Coding & Documentation Conventions

## C# Style

- **Nullable enabled**: Use `string?` where appropriate.
- **Implicit usings**: Keep files clean; avoid redundant usings.
- **Namespaces**: `Cyberpilot.*` (file-scoped preferred).
- **Logging**: Inject `ILogger<T>`; log at appropriate levels.
- **Exceptions**: Prefer specific exception types; avoid swallowing exceptions.

## XML Documentation

- All **public** classes/methods/properties **must** have `///` XML comments.
- Summaries should be concise, starting with a verb.
- Include `<param>`, `<returns>`, and `<remarks>` when helpful.
- The build generates `Cyberpilot.Web.xml` (see `web/Cyberpilot.Web.csproj`).

## Controllers & Actions

- Return appropriate `IActionResult` (e.g., `View()`, `RedirectToAction`).
- Apply attributes for caching, authorization, and routing explicitly.
- Validate inputs and model state (`ModelState.IsValid`).

## Views

- Use strongly-typed models where possible (`@model` directive).
- Keep logic in controllers/services; minimize code in views.

## Configuration

- Use `appsettings.json` and environment-specific overrides (e.g., `appsettings.Development.json`).
- Do **not** commit secrets; use environment variables or secret managers.

## Testing

- Name test projects `*.Tests`.
- Use `dotnet test` and keep tests idempotent.
- Prefer unit tests for controllers/services; add integration tests for key flows.

## Git Hygiene

- `.gitignore` excludes build artifacts, secrets, IDE files.
- Commit small, focused changes with clear messages.

## Documentation Process

- Update `README.md` and relevant docs in `docs/` for notable changes.
- The **Documentation Helper** agent will flag missing XML docs on new public code.
