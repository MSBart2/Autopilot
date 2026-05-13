# GitHub Automation Instructions

These instructions apply when working under `.github/`.

## Ownership

- This folder owns local custom agents, prompts, skills, path-specific instructions, issue templates, and GitHub Agentic Workflow sources.
- Keep automation changes tightly scoped and easy to audit. Small mistakes here can affect issue handling, PR review, and delivery workflows.
- Prefer updating reusable skills or prompts over duplicating long instructions across workflow files.

## Agent Customization Files

- Keep `.agent.md`, `.prompt.md`, `.instructions.md`, and `SKILL.md` files concise, discoverable, and task-specific.
- Use clear descriptions and trigger language so custom agents and skills are selected for the right jobs.
- For `.instructions.md` files, use narrow `applyTo` patterns unless the rule genuinely applies everywhere.

## Workflows

- Use the `gh-aw-compile` skill for `.github/workflows/cloud-*.md` source files.
- Before compiling gh-aw workflows, run `& .\scripts\check-gh-aw-version.ps1` from the repository root.
- Delete the corresponding `.lock.yml` files before `gh aw compile` so lockfiles pin a current AWF binary.
- Never hand-edit `.github/workflows/*.lock.yml` files.
- Plain `.yml` workflows are normal GitHub Actions files, not gh-aw source files.

## Validation

- For customization-only changes, check Markdown/frontmatter syntax and use the Chat Customizations diagnostics view when available.
- For gh-aw workflow changes, include both changed `.md` sources and regenerated `.lock.yml` files.