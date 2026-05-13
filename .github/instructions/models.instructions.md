---
applyTo: "web/Models/**"
---

# Model Development Instructions

## Model Guidelines

- Use data annotations for validation (`[Required]`, `[StringLength]`, etc.)
- Prefer record types for immutable data transfer objects
- Use nullable reference types appropriately (`string?` for optional properties)
- Keep models focused and single-purpose

## Naming Conventions

- Suffix view models with `ViewModel` (e.g., `HomeViewModel`)
- Suffix request/response DTOs appropriately
- Use PascalCase for public properties

## Documentation

- Add XML documentation comments (`///`) to all public model classes and properties
- Document validation constraints and business rules
- Include examples in remarks when helpful

## Validation

- Use `[Required]` for mandatory fields
- Use `[StringLength]` or `[MaxLength]` for text fields
- Use `[Range]` for numeric constraints
- Create custom validation attributes for complex rules
