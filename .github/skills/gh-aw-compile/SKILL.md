---
name: gh-aw-compile
description: 'Safely update GitHub Agentic Workflow files by deleting existing .lock.yml files before running gh aw compile. Use when editing .github/workflows/cloud-*.md, recompiling gh-aw workflows, refreshing lockfiles, fixing stale workflow locks, or validating agentic workflow changes.'
argument-hint: 'Optional: which cloud workflow files changed'
---

# gh-aw Compile Refresh

Use this skill when working on GitHub Agentic Workflow source files in `.github/workflows/cloud-*.md`.

The goal is to avoid stale or defunct lockfiles by deleting the existing compiled `.lock.yml` files before recompiling with `gh aw compile`.

## When to Use

- After editing any `.github/workflows/cloud-*.md` workflow source file
- When `gh aw compile` needs to regenerate lockfiles with the latest AWF pin
- When reviewing or preparing workflow changes for commit
- When troubleshooting stale `.lock.yml` output or AWF version drift

## Procedure

### Step 1: Confirm Scope

Identify which workflow source files changed under `.github/workflows/`.

Focus on agentic workflow source files such as:
- `cloud-cyberpilot.md`
- `cloud-triage.md`
- `cloud-plan.md`
- `cloud-implement.md`
- `cloud-review.md`
- `cloud-docs.md`

Do not treat plain YAML workflows like `cloud-finish.yml` as gh-aw compile targets.

### Step 2: Check Local gh-aw Version

Before deleting lockfiles or recompiling, verify that the local `gh aw` extension is current.

Run this from the repository root.

Run:

```powershell
& .\scripts\check-gh-aw-version.ps1
```

If the script reports `UPDATE NEEDED`, stop and upgrade `gh-aw` first. Regenerating lockfiles with an outdated local compiler can reproduce stale AWF pins even if you deleted the old `.lock.yml` files.

If the script cannot determine the latest version because GitHub release access is blocked, fix local `gh` authentication first and rerun the check before compiling.

### Step 3: Delete Existing Lockfiles First

Before running `gh aw compile`, delete the existing compiled lockfiles.

Default command:

```powershell
Remove-Item .github/workflows/cloud-*.lock.yml -ErrorAction SilentlyContinue
```

If the task is tightly scoped and only one or two workflow sources changed, it is acceptable to delete only the corresponding lockfiles. If scope is unclear, delete all `cloud-*.lock.yml` files to force a full fresh pin.

Why this matters:
- Recompiling without deletion can preserve an old AWF binary pin
- Old pinned releases can disappear upstream and later fail with 404-style workflow errors
- Deleting first forces a fresh lockfile with the latest available pinned version

### Step 4: Recompile

Run:

```powershell
gh aw compile
```

Run this from the repository root.

### Step 5: Validate Result

Treat compilation as successful only if:
- `gh aw compile` exits successfully
- The edited `.md` workflows compile without errors
- The matching `.lock.yml` files are regenerated
- The regenerated lockfiles are included in the working tree changes

If compile fails, stop and fix the workflow source rather than editing the `.lock.yml` file directly.

### Step 6: Pre-Commit Check

Before committing workflow changes:
- Confirm the `.md` source files are present in the diff
- Confirm the regenerated `.lock.yml` files are also present
- Do not commit `.md` changes without the corresponding `.lock.yml` updates

## Decision Rules

- If only `cloud-finish.yml` changed: do not run this skill; that file is plain YAML, not gh-aw source
- If any `cloud-*.md` file changed: run `& .\scripts\check-gh-aw-version.ps1`, then delete lockfiles, then run `gh aw compile`
- If both `.md` workflow files and their `.lock.yml` files already changed: still prefer deleting the current lockfiles and recompiling rather than trusting the existing generated output
- If the version check reports that the local compiler is outdated: upgrade `gh-aw` before compiling

## Completion Criteria

The task is complete when all of the following are true:
- The local `gh aw` version check passed
- Old lockfiles were deleted before compilation
- `gh aw compile` completed successfully
- Updated `.lock.yml` files were regenerated
- Source and compiled workflow files are both ready to commit together

## Guardrails

- Never edit `.lock.yml` files by hand
- Never skip lockfile regeneration after changing `cloud-*.md`
- Never skip the local `gh aw` version check before compiling
- Prefer deleting all `cloud-*.lock.yml` files when in doubt
- Use the repository root as the working directory when running the commands

## Example Prompts

- `/gh-aw-compile after editing cloud-review.md and cloud-docs.md`
- `/gh-aw-compile refresh the cloud workflow lockfiles`
- `/gh-aw-compile validate my gh aw workflow changes before commit`
