# `.github/prompts/` — Reusable Copilot Prompt Files

This folder contains **prompt files** — reusable, parameterized Copilot Chat prompts that execute a defined workflow when invoked. Unlike custom agents (which define a persistent persona with tool restrictions), prompt files are one-shot task runners: invoke one, it runs, it's done.

## How Copilot Discovers and Activates Prompt Files

Copilot scans `.github/prompts/` for any file ending in `.prompt.md`. Discovered files appear as slash commands in Copilot Chat — type `/` followed by the filename (without the `.prompt.md` extension) to invoke them.

**Activation paths:**

| Context | How to invoke |
|---------|--------------|
| VS Code Copilot Chat (Agent mode) | Type `/filename` or select from the slash-command picker |
| GitHub Copilot cloud agent (github.com) | Reference by name in a prompt or assign to an issue |
| Copilot CLI | Reference via `--prompt-file` or slash command |

Prompt files support **variable substitution** using `${variableName}` syntax. When a prompt is invoked, Copilot prompts for any undefined variables before running.

## File Format

Prompt files are plain Markdown with an optional YAML frontmatter block:

```markdown
---
mode: agent          # 'ask', 'edit', or 'agent' (default: agent)
tools: ['read', 'execute']   # Optional: restrict available tools for this prompt
description: "Short description shown in the slash-command picker"
---

# Prompt Title

Instructions for Copilot to follow when this prompt is invoked...

Use ${variableName} for runtime substitution.
```

**Mode values:**

| Mode | Behavior |
|------|----------|
| `ask` | Copilot answers without making edits (read-only) |
| `edit` | Copilot makes targeted file edits based on the prompt |
| `agent` | Copilot runs autonomously with full agentic tool use (default) |

## Prompts in This Repository

### `snarky-commit.prompt.md` — Snarky Commit & PR

**Invoke with:** `/snarky-commit`

An end-to-end pre-commit quality gate and Git workflow wrapped in a healthy dose of attitude. When invoked, Copilot:

1. **Analyzes the git diff** — reads changed files to understand what was done
2. **Generates snarky messages** — crafts a witty commit message describing the changes and a spicy PR title
3. **Runs the commit script** — executes `./scripts/commit.sh` with the generated messages, which internally:
   - Builds the project in Release configuration
   - Runs all unit tests
   - Checks 35% controller line coverage with Coverlet MSBuild and warns if it is below threshold
   - Smoke-tests that the app actually starts
   - Creates a feature branch (never commits to `main` directly)
   - Commits and pushes with upstream tracking
   - Opens a pull request via the GitHub CLI (`gh`)
4. **Reports results** — celebrates a successful commit with enthusiasm

**Script parameters accepted:**

```bash
# Custom commit message + custom PR title
./scripts/commit.sh "Your message here" "Your PR title here"

# Custom commit message, random PR title
./scripts/commit.sh "Fixed the thing 💪"

# Fully random (script picks both)
./scripts/commit.sh
```

**Prerequisites:**

- .NET SDK installed
- Git repository initialized with a remote
- GitHub CLI (`gh`) installed and authenticated (`gh auth login`)

**Quality gates — the script exits immediately for blocking failures; coverage warns and continues:**

- Build fails
- Any test fails
- Code coverage drops below 35% controller line coverage (warns and continues)
- Application fails to start in the smoke test

**Safety guarantees:**

- Never commits directly to `main` or `master` — always creates a timestamped feature branch (`feature/absolutely-legendary-YYYYMMDD-HHMMSS`)
- Validates all changes before writing any commits
- Pushes with `--set-upstream` so the branch tracks its remote counterpart

## Adding New Prompt Files

1. Create a `.prompt.md` file in this folder
2. Add optional YAML frontmatter (`mode`, `tools`, `description`)
3. Write the instructions in Markdown
4. The file is immediately available as a slash command in Copilot Chat — no registration needed

**When to use a prompt file vs. a custom agent:**

| Use case | Use |
|----------|-----|
| Repeatable one-shot task (build, commit, scaffold) | Prompt file |
| Persistent persona with tool restrictions | Custom agent (`.github/agents/`) |
| Multi-step workflow you want to run on demand | Prompt file |
| Specialized reviewer or planner role | Custom agent |

## References

- [VS Code Docs: Create reusable prompt files](https://code.visualstudio.com/docs/copilot/customization/prompt-files)
- [GitHub Docs: Custom agents configuration](https://docs.github.com/en/copilot/reference/custom-agents-configuration)
