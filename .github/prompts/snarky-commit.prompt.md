---
name: commit
agent: agent
---

# Snarky Commit

Build, test, check coverage, commit with attitude, and open a PR with controlled sass.

## What this does

This prompt will execute a comprehensive quality check and commit workflow:

1. **Build** the application in Release configuration
2. **Run all unit tests** to ensure nothing is broken
3. **Check code coverage** to warn if controller line coverage drops below 35%
4. **Smoke test** the application to verify it actually runs
5. **Commit intended changes** with your custom snarky commit message (or a generated one)
6. **Push to remote** on the current branch


## Usage Options

Use this prompt when you are ready to validate, commit and push.


- Provide a preferred commit message if you want specific wording.
- If no wording is provided, generate snarky but accurate text from the diff.
- Include only changes that belong to the current task; leave unrelated work alone unless the user explicitly includes it.

## Quality Gates

The script exits if blocking gates fail; coverage warns and continues:

- ❌ Build fails
- ❌ Any test fails
- ⚠ Code coverage drops below 35% controller line coverage (warns and continues)
- ❌ Application fails to start

## Safety Features

- Validates all changes before committing
- Reviews the working tree before staging
- Pushes with upstream tracking configured after the commit succeeds
- Comprehensive quality gates before commit

## Sample Output

**Default Commit Messages (randomly selected if none provided):**

- "Fixed the thing. You know, THAT thing. 🙄"
- "This commit is chef's kiss 👌 Your code review? Probably not."
- "Made the build green. Made the reviewers green with envy 💚"

**Or use your own!** Provide runtime wording when you want a specific flavor of snark. 🎨


## Agent Instructions

When this prompt is invoked:

1. **Generate creative snarky messages** based on the changes:
   - Analyze the git diff to understand what was changed
   - Create a witty, snarky commit message that describes the changes
   - Keep them PG-13 but make them pop! 🔥

2. **Run quality gates** (build, test, coverage checks)

3. **Commit and push** the intended changes on the current branch:
   ```bash
   cd /workspaces/Cyberpilot && git status --short && git add <intended files> && git commit -m "your generated commit message" && git push
   ```

4. **Report the results** with enthusiasm and celebrate the successful commit!

**Message Style Guidelines:**
- Commit messages: Describe what was done but make it sassy
- Use emojis liberally 💅✨🔥
- Be playful but professional enough for a real repo
- Examples:
  - Commit: "Refactored the entire codebase. It's prettier than your portfolio now 💅"
