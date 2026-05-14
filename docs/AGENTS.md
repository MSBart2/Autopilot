# Documentation Instructions

These instructions apply when working in the documentation folder.

## Ownership

- Keep operational, pipeline, configuration, testing, and CI/CD documentation aligned with the current code and workflows.
- Cross-link related docs instead of repeating long explanations in multiple files.
- Update [`../architecture.md`](../architecture.md) when documentation changes reveal an architecture drift or a meaningful behavior change.

## Writing Style

- Use clear, concise language in present tense and active voice.
- Define acronyms on first use and state prerequisites near the top.
- Use code fences with language tags for commands, configuration, and examples.
- Prefer concrete commands and paths over vague descriptions.
- Add tables of contents only for longer documents where navigation benefits the reader.

## Validation

- Run `bash ./scripts/lint-docs.sh` from the repository root when Markdown formatting changes are broad enough to justify it.
- For command examples, prefer commands already documented in [`../architecture.md`](../architecture.md), [`../README.md`](../README.md), or the relevant project `AGENTS.md` file.