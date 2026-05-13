---
applyTo: "web/Controllers/**"
---

# Controller Development Instructions

## MVC Controller Guidelines

- Inherit from `Controller` base class for view-rendering controllers
- Use `[Route]` and HTTP verb attributes (`[HttpGet]`, `[HttpPost]`, etc.) for explicit routing
- Return appropriate `IActionResult` types (`View()`, `RedirectToAction()`, `NotFound()`, etc.)
- Validate `ModelState.IsValid` before processing form submissions
- Use dependency injection for services via constructor injection

## Security Requirements

- Apply `[Authorize]` attribute on controllers/actions requiring authentication
- Use `[ValidateAntiForgeryToken]` on POST actions to prevent CSRF
- Validate and sanitize all user inputs
- Never trust client-side data
- For pipeline replay/reset actions, reject delivered runs server-side so merged code history is not scrubbed accidentally

## Documentation

- Add XML documentation comments (`///`) to all public controller methods
- Document expected parameters and return types
- Include remarks for complex business logic

## Error Handling

- Use try-catch blocks for external service calls
- Log exceptions using injected `ILogger<T>`
- Return user-friendly error views for production
