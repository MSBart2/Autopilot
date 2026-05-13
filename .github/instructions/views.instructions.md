---
applyTo: "web/Views/**"
---

# View Development Instructions

## Razor View Guidelines

- Use strongly-typed views with `@model` directive
- Keep logic minimal in views - move complex logic to controllers or services
- Use tag helpers over HTML helpers where available
- Leverage partial views for reusable components

## Layout and Structure

- Extend the shared `_Layout.cshtml` for consistent page structure
- Use `@section` for page-specific scripts and styles
- Organize views in folders matching controller names

## Security

- Always encode user-generated content (Razor does this by default)
- Use `@Html.AntiForgeryToken()` in forms
- Avoid inline JavaScript with user data

## Accessibility

- Use semantic HTML elements
- Include `alt` attributes on images
- Ensure proper heading hierarchy
- Use ARIA attributes where appropriate
