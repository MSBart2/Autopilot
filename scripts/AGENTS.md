# Script Instructions

These instructions apply when working in the scripts folder.

## Ownership

- Scripts should be small, repeatable helpers for repository maintenance, validation, and automation.
- Keep Windows PowerShell support first-class because this repo is commonly worked from Windows.
- Preserve existing Bash helpers when changing cross-platform workflows.

## Implementation Rules

- Prefer explicit parameters, clear errors, and deterministic exit codes.
- Do not hide failing commands unless the script intentionally probes optional state.
- Avoid printing secrets, tokens, full environment dumps, or sensitive local paths.
- Keep scripts runnable from the repository root unless a script clearly documents a different working directory.

## Validation

- For PowerShell scripts, run the script or the narrowest safe command path from the repository root.
- For Bash scripts, validate syntax or run them in an environment where Bash is available.
- When a script changes documented build, test, or workflow behavior, update the matching docs or root instructions.