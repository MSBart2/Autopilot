# `.github/agents/` — Custom Copilot Agents

This folder contains **custom agent definitions** for GitHub Copilot. Each file defines a specialized AI persona that can be selected in VS Code's Copilot Chat, in the GitHub Copilot cloud agent on GitHub.com, or invoked as a subagent by the orchestrator.

## AI-SDLC Pipeline

This repository implements a **fully-autonomous AI-driven SDLC pipeline** that runs both remotely (via GitHub Agentic Workflows) and locally (via Copilot CLI).

```
🏷️ Triage → 📋 Plan → 🔨 Implement → 👀 Review → 🚀 Land
```

**Local usage (Copilot CLI):** Say "run issue 135" — the `cyberpilot` agent auto-chains through all stages.

**Remote usage (GitHub Actions):** Apply the `cloud/triage-requested` label to an issue — the `pipeline-triage.md` workflow kicks off the same flow on GitHub runners.

Each stage posts comments on the GitHub issue, providing full transparency of AI thinking and decisions.

## How Copilot Discovers and Activates These Agents

GitHub Copilot automatically scans the `.github/agents/` folder for any file ending in `.agent.md`. No registration or configuration is required — just drop a properly formatted file here and it appears in the agents dropdown in Copilot Chat.

**Activation paths:**

| Context | How to activate |
|---------|----------------|
| VS Code Copilot Chat | Select agent from the agents dropdown at the bottom of the Chat panel |
| GitHub.com cloud agent | Choose agent from the Copilot Chat interface on github.com (requires `target: github-copilot`) |
| Subagent invocation | The `orchestrator` agent calls other agents via the `agent` tool |

## Agent File Format

Each `.agent.md` file uses YAML frontmatter to declare its identity and capabilities, followed by Markdown instructions that define the agent's behavior:

```markdown
---
name: optional-display-name         # Defaults to filename without extension
description: "Required. Short description shown in the agents dropdown."
tools: ['read', 'edit', 'search']   # Tool access list (empty list disables ALL tools)
agents: ['*']                        # Optional: subagents this agent can invoke (* = all)
model: claude-sonnet-4-5 (copilot)  # Optional: pin to a specific model or array of models
target: vscode                       # Optional: 'vscode' or 'github-copilot' (default: both)
user-invocable: true                 # Optional: show in agents dropdown (default: true)
disable-model-invocation: false      # Optional: prevent subagent invocation (default: false)
argument-hint: "Describe your task"  # Optional: hint text shown in the chat input field
---

# Agent Name

Instructions, persona, and behavioral guidelines in Markdown...
```

### Tool Sets and Tools Reference

| Tool Set | What it grants | Individual tools |
|----------|---------------|------------------|
| `read` | Read files in your workspace | `read/readFile`, `read/problems`, `read/terminalLastCommand`, `read/terminalSelection`, `read/getNotebookSummary`, `read/readNotebookCellOutput` |
| `edit` | Create and modify files | `edit/createFile`, `edit/editFiles`, `edit/createDirectory`, `edit/editNotebook` |
| `search` | Search files and text | `search/codebase`, `search/textSearch`, `search/fileSearch`, `search/listDirectory`, `search/usages`, `search/changes` |
| `execute` | Run shell commands and tasks | `execute/runInTerminal`, `execute/getTerminalOutput`, `execute/createAndRunTask`, `execute/runNotebookCell`, `execute/testFailure` |
| `agent` | Invoke other custom agents as subagents | `agent/runSubagent` |
| `web` | Fetch URLs and access web content | `web/fetch` |
| `todos` | Create and manage task lists | *(standalone tool)* |
| `browser` | (Experimental) Interact with integrated browser | Navigate, screenshot, click, type, hover, drag |

> **Important:** `tools: []` disables **all** tools. Omit the `tools` property entirely to grant access to all available tools.
>
> You can reference entire tool sets (e.g., `search`) or individual tools (e.g., `search/usages`). Tool sets include MCP tools and extension tools in addition to built-in tools.

## Agents in This Repository

### `cyberpilot.agent.md`

**Tools:** `read`, `search`, `execute`, `github`, `agent`, `web`
**Agents:** `triage`, `plan`, `implement`, `pipeline-review`, `docs`, `summary`, `deliver`
**Argument hint:** "Say 'run issue 135' to run the full pipeline on an issue"

The entry point for the autonomous pipeline. Auto-chains through all stages (triage → plan → implement → pipeline review → docs → summary → deliver) without pausing. Handles retry loops if review requests changes (max 2 cycles). Choose this agent to run the full pipeline on an issue.

---

### `triage.agent.md`

**Tools:** `read`, `search`, `github`

Classifies issues by type, difficulty, priority, and scope. Posts a triage comment and applies labels. Choose this agent to classify a single issue without running the full pipeline.

---

### `plan.agent.md`

**Tools:** `read`, `search`, `github`, `agent`
**Agents:** `backend`, `frontend`, `security-implementer`

Researches the codebase, produces detailed implementation plans with file-level task lists, creates feature branches. Choose this agent to plan an issue without implementing it.

---

### `implement.agent.md`

**Tools:** `read`, `edit`, `search`, `execute`, `github`, `agent`, `todos`
**Agents:** `backend`, `frontend`, `security-implementer`, `testing`, `build-validator`

Executes plans by delegating to specialist agents, writing code, committing, and creating PRs. Choose this agent to implement a planned issue.

---

### `pipeline-review.agent.md`

**Tools:** `read`, `search`, `github`, `agent`
**Agents:** `security-reviewer`, `code-quality-reviewer`, `build-validator`

Pipeline PR review orchestrator covering architecture, security, code quality, test coverage, and documentation. Delegates specialist checks and makes approve/request-changes decisions. Choose this agent to run the pipeline review gate for a PR.

---

### `summary.agent.md`

**Tools:** `read`, `search`, `github`

Synthesizes a stakeholder-ready summary and changelog package from issue, PR, review, and docs context. Choose this agent to produce release-note-style summaries without merging.

---

### `deliver.agent.md`

**Tools:** `read`, `search`, `execute`, `github`

Merges approved PRs to main and updates the issue label to `local/done`. Does not close the issue. Choose this agent to deliver an approved PR.

---

### `backend.agent.md`

**Tools:** `read`, `edit`, `search`, `execute`

Expert in ASP.NET Core MVC backend development. Handles work in `Controllers/`, `Models/`, `Services/`, `Middleware/`, and `Program.cs`. Applies MVC patterns, dependency injection, proper `IActionResult` usage, async/await, `[Authorize]` attributes, and `ModelState` validation. Choose this agent when changing C# server-side code.

---

### `frontend.agent.md`

**Tools:** `read`, `edit`, `search`

Expert in Razor Views, layouts, CSS, and JavaScript. Handles files in `Views/`, `wwwroot/css/`, `wwwroot/js/`, and static assets. Applies strongly-typed view patterns (`@model`), tag helpers, Bootstrap integration, and client-side validation. Choose this agent for UI changes, layout updates, or view logic.

---

### `security-implementer.agent.md`

**Tools:** `read`, `edit`, `search`, `execute`

Implementation expert for authentication, authorization, OWASP Top 10 mitigations, security headers, HTTPS enforcement, secrets management, and CSRF protection. Works across the whole codebase. Choose this agent when adding authentication, hardening security, or fixing security findings.

---

### `security-reviewer.agent.md`

**Tools:** `read`, `search`, `execute`

Review-focused security scanning agent. Audits the codebase for vulnerabilities using a structured OWASP checklist — covering authentication, input validation, SQL injection, secrets exposure, and HTTPS configuration. Does **not** modify code; produces a security report with prioritized remediation steps. Choose this agent for security reviews and audits.

---

### `testing.agent.md`

**Tools:** `read`, `edit`, `search`, `execute`

Expert in xUnit, Moq, WebApplicationFactory, FluentAssertions, and Playwright. Writes and improves unit tests, integration tests, and end-to-end tests. Works within the `tests/` directory. Choose this agent to add test coverage, fix failing tests, or set up test infrastructure.

---

### `docs.agent.md`

**Tools:** `edit`, `search`, `todos`, `agent`, `web`
**Agents:** `code-quality-reviewer`, `security-reviewer`

Expert in Markdown documentation, XML doc comments, README files, Swagger/OpenAPI, and architecture diagrams (Mermaid). Works in `docs/`, project root, and source files. Choose this agent to write or update documentation, generate XML comments, or improve README clarity.

---

### `code-quality-reviewer.agent.md`

**Tools:** `read`, `search` *(read-only — no file modifications)*

Read-only code quality reviewer. Checks MVC architectural patterns, dependency injection usage, async patterns, security-adjacent hygiene, XML docs, and code maintainability. Produces detailed review feedback without modifying any files. Choose this agent to get a maintainability review before submitting a PR.

---

### `build-validator.agent.md`

**Tools:** `read`, `search`, `execute` *(no file edits)*

Analyzes `.csproj` files for correct SDK, target framework, nullable settings, and NuGet package health. Runs builds and reports dependency vulnerabilities. Does not modify project files. Choose this agent to validate build configuration or diagnose dependency issues.

---

## Pipeline Labels

These labels track pipeline progress on GitHub issues:

| Label | Stage |
|-------|-------|
| `cloud/triage-requested` | Trigger: starts the remote pipeline (via GitHub Actions) |
| `local/triage` | Being classified (local agents) |
| `local/planning` | Plan being created (local agents) |
| `local/implementing` | Code being written (local agents) |
| `local/review` | PR under review (local agents) |
| `local/done` | Pipeline complete, issue closed (local agents) |

## GitHub Actions Workflows

The remote pipeline uses GitHub Agentic Workflows (`.github/workflows/pipeline-*.md`):

| Workflow | Trigger |
|----------|---------|
| `pipeline-triage.md` | `cloud/triage-requested` label applied |
| `pipeline-plan.md` | Dispatched by triage |
| `pipeline-implement.md` | Dispatched by plan |
| `pipeline-review.md` | PR opened / review requested |
| `pipeline-deploy.md` | PR closed (merged) |
| `pipeline:blocked` | Needs human | Red |
| `pipeline:failed` | Failed (exhausted retries) | Red |

### Retry Strategy

Each stage gets **2 automatic retry attempts**. On each retry, the pipeline:
1. Posts a narrative explaining the retry strategy
2. Increments the attempt counter in state
3. Re-triggers the failed stage with fresh context

After 2 failures: applies `pipeline:failed`, posts a comprehensive diagnosis, and requests human intervention.

## References

- [VS Code Docs: Custom Agents in VS Code](https://code.visualstudio.com/docs/copilot/customization/custom-agents)
- [VS Code Docs: Custom Agent File Structure](https://code.visualstudio.com/docs/copilot/customization/custom-agents#_custom-agent-file-structure)
- [VS Code Docs: Use Tools with Agents](https://code.visualstudio.com/docs/copilot/agents/agent-tools)
- [VS Code Docs: Chat Tools Reference](https://code.visualstudio.com/docs/copilot/reference/copilot-vscode-features#_chat-tools)
- [GitHub Docs: Create Custom Agents for Organizations](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent/create-custom-agents)
