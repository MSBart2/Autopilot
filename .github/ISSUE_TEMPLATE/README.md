# `.github/ISSUE_TEMPLATE/` — GitHub Issue Templates

This folder contains **structured issue templates** that appear when a contributor selects "New Issue" in the GitHub repository. Each template is a YAML form definition that guides reporters into providing the right information for a specific type of issue. The `config.yml` file controls global issue creation settings.

## How GitHub Activates These Templates

GitHub automatically scans `.github/ISSUE_TEMPLATE/` and presents the templates as options on the "New Issue" chooser page. No workflow or action is required — this is a native GitHub feature.

**When a user clicks "New Issue":**

1. GitHub reads all `.yml` files in this folder
2. It presents a chooser screen listing each template by its `name` field
3. The user selects a template, which pre-fills the issue body as a structured form
4. The `labels` defined in the template are automatically applied when the issue is submitted
5. `config.yml` controls whether users can open blank (unstructured) issues

## File Format

Templates use GitHub's issue form schema — a YAML file with metadata and an array of form fields:

```yaml
name: "Display name shown in the chooser"
description: "One-line description under the name"
labels: [label1, label2]       # Labels auto-applied on submission
title: "[PREFIX] <placeholder>" # Default issue title
body:
  - type: markdown              # Static informational text
  - type: input                 # Single-line text field
  - type: textarea              # Multi-line text field
  - type: checkboxes            # Checkbox list
  - type: dropdown              # Select menu
```

## Templates in This Repository

### `bug_report.yml` — Bug Report

**Label applied:** `bug`
**Default title prefix:** `[BUG]`

Guides reporters through filing a reproducible bug. Collects:

- **Summary** — Short description of the symptom
- **Steps to reproduce** — Numbered steps with specific URLs, payloads, and configs
- **Expected behavior** — What the user expected to happen
- **Actual behavior** — What actually happened
- **Logs / stack traces** — Paste or link to relevant error output
- **Environment** — OS, browser, .NET SDK version
- **Impact** — Severity selector (Blocking / High / Medium / Low)

Used by: developers, QA, and end users reporting regressions or broken functionality.

---

### `feature_request.yml` — Feature Request

**Label applied:** `enhancement`
**Default title prefix:** `[FEAT]`

Structures a request for new functionality. Collects:

- **Summary** — Concise description of the desired capability
- **Problem / Opportunity** — The underlying problem the feature solves and who benefits
- **Proposed solution** — Desired behavior, APIs, UI changes, or config
- **Alternatives considered** — Other approaches that were evaluated

Used by: contributors, product owners, and developers proposing improvements or new capabilities.

---

### `security.yml` — Security Issue

**Label applied:** `security`
**Default title prefix:** `[SECURITY]`

Provides a structured path for reporting vulnerabilities. The template header advises reporters to avoid sharing sensitive details publicly and suggests private reporting for critical issues. Collects:

- **Summary** — High-level description (without exploitable detail)
- **Impact / Risk** — Potential consequences and affected users or systems
- **Steps to reproduce / Evidence** — Enough detail to verify the issue

> **Note:** For critical vulnerabilities, prefer GitHub's private vulnerability reporting feature (Security → Report a vulnerability) rather than this public template.

Used by: security researchers, developers, and automated scanners identifying OWASP-class vulnerabilities.

---

### `documentation.yml` — Documentation Request

**Label applied:** `documentation`
**Default title prefix:** `[DOCS]`

Tracks gaps, inaccuracies, or missing content in project documentation. Collects:

- **Summary** — What documentation is needed or wrong
- **Location** — Specific file(s) and section(s) to update (e.g., `docs/architecture.md#health-checks`)
- **Details / Outline** — Key points to cover, examples, diagrams, or corrections

Used by: anyone who finds documentation missing, outdated, or unclear.

---

### `config.yml` — Issue Chooser Configuration

Controls the behavior of the "New Issue" chooser page:

```yaml
blank_issues_enabled: false    # Prevents unstructured blank issues
contact_links:
  - name: ❓ Questions / Support
    url: https://github.com/rbmathis/Cyberpilot/discussions
    about: Ask questions and get help in Discussions
```

- **`blank_issues_enabled: false`** — Forces all new issues through a template, ensuring consistent structure and required fields.
- **`contact_links`** — Adds a "Questions / Support" link on the chooser page that redirects to GitHub Discussions, keeping the issue tracker focused on actionable work items.

## Integration with the Issue Triage Workflow

When a new issue is submitted using any of these templates, the **`issue-triage-agent.yml`** GitHub Actions workflow fires automatically (trigger: `issues: [opened]`). It reads the issue title and body, classifies it as easy / moderate / hard using keyword heuristics, applies the appropriate difficulty label, and posts a structured triage comment.

The `.github/agents/issue-helper.agent.md` custom Copilot agent provides AI-assisted triage in Copilot Chat, complementing the automated workflow.

## References

- [GitHub Docs: Configuring issue templates](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/configuring-issue-templates-for-your-repository)
- [GitHub Docs: Issue form syntax](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-issue-forms)
- [GitHub Docs: Private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing/privately-reporting-a-security-vulnerability)
