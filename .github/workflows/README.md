# `.github/workflows/` — GitHub Actions Workflows

This folder contains all **GitHub Actions workflow definitions** for the Cyberpilot repository. Each `.yml` file defines one or more jobs that run automatically in response to GitHub events (push, pull request, schedule, etc.) or can be called by other workflows.

## How GitHub Activates These Workflows

GitHub scans `.github/workflows/` on every event and evaluates the `on:` trigger of each workflow file. If the event matches, the workflow is queued. Workflows run on GitHub-hosted runners (Ubuntu by default) unless configured otherwise.

---

## Workflows in This Repository

### `dotnet.yml` — .NET Build and Test

**Triggers:**
- `push` to `main` or `develop`
- `pull_request` targeting `main`

**Jobs:**

| Job | Purpose |
|-----|---------|
| `lint-docs` | Runs [Super-Linter](https://github.com/github/super-linter) in Markdown-only mode (`VALIDATE_MARKDOWN: true`) to enforce `markdownlint` rules across all `.md` files |
| `build` | Calls the reusable `build-and-test.yml` workflow — restores, builds, runs tests, publishes artifact. Playwright install is skipped on PRs to keep checks fast |
| `security-scan` | Runs `dotnet list package --vulnerable --include-transitive` after the build to detect NuGet packages with known CVEs |

**Notes:**
- Playwright browsers are only installed on push to `main`/`develop`, not on pull request runs, to reduce CI time.
- The `security-scan` job depends on `build` completing successfully (`needs: build`).

---

### `build-and-test.yml` — Reusable Build Workflow

**Triggers:** Called by other workflows only (`workflow_call`) — never runs directly

**Inputs:**

| Input | Default | Purpose |
|-------|---------|---------|
| `dotnet-version` | `9.0.x` | .NET SDK version to install |
| `install-playwright` | `true` | Whether to install Playwright browsers |

**What it does:**

1. Checks out the repository
2. Installs the specified .NET SDK
3. Restores NuGet packages (with cache keyed on `*.csproj` and `*.sln` hashes)
4. Caches `.dotnet/tools` to avoid reinstalling global tools on every run
5. Builds in Release configuration
6. Optionally installs Playwright browsers and their OS dependencies (with cache)
7. Runs all test projects via `dotnet test`
8. Publishes the application to `./publish`
9. Uploads the published app as an Actions artifact named `published-app`

**Cache strategy:**

| Cache | Key |
|-------|-----|
| NuGet packages (`~/.nuget/packages`) | OS + hash of all `.csproj`/`.sln` files |
| .NET tools (`~/.dotnet/tools`) | OS + hash of all `.csproj`/`.sln` files |
| Playwright browsers (`~/.cache/ms-playwright`) | OS + hash of `Cyberpilot.Web.PlaywrightTests.csproj` |

---

### `deploy.yml` — Deploy to Production

**Triggers:**
- `push` to `main`
- `push` of any tag matching `v*` (e.g., `v1.2.3`)

**Jobs:**

| Job | Depends on | Purpose |
|-----|-----------|---------|
| `build` | — | Calls `build-and-test.yml` to produce the published artifact |
| `docker` | `build` | Downloads the artifact, builds a Docker image, pushes to GitHub Container Registry (`ghcr.io`) |
| `deploy` (implied) | `docker` | Deploys to Azure Web App `Cyberpilot-app` using the published artifact |

**Docker image tagging:**
- Reads the `VERSION` file for the semantic version
- Tags the image with the version and the git SHA for traceability
- Pushes to `ghcr.io/<owner>/<repo>`

**Required secrets/configuration:**

| Variable | Description |
|----------|-------------|
| `GITHUB_TOKEN` | Auto-provided — used to push to GHCR and create releases |
| `AZURE_WEBAPP_NAME` | Set to `Cyberpilot-app` in workflow env |

---

### `version.yml` — Semantic Versioning

**Triggers:**
- `pull_request` closed (merged only) targeting `main`

**Condition:** Only runs when `github.event.pull_request.merged == true` — ignored for closed-but-not-merged PRs.

**What it does:**

1. Reads the current version from the `VERSION` file at the repo root
2. Inspects the PR body for bump indicators (using a safe `env:` variable — **not** inline interpolation, to prevent script injection)
3. Calculates the next semantic version:
   - `BREAKING CHANGE` or `#major` in PR body → major bump (x.0.0)
   - `#minor`, `feat:`, or `feature` in PR body → minor bump (0.x.0)
   - Everything else → patch bump (0.0.x)
4. Writes the new version back to `VERSION`
5. Commits and pushes directly to `main` with `[skip ci]` to avoid triggering another build loop
6. Creates and pushes a Git tag (`v<version>`)
7. Creates a GitHub Release with auto-generated release notes and a changelog comparison link

**Version bump reference:**

| PR body contains | Bump | Example |
|-----------------|------|---------|
| `BREAKING CHANGE` or `#major` | Major | `1.0.0` → `2.0.0` |
| `#minor`, `feat:`, or `feature` | Minor | `1.0.0` → `1.1.0` |
| Anything else | Patch | `1.0.0` → `1.0.1` |

**Security note:** The PR body is passed via `env:` rather than direct `${{ }}` interpolation in the `run:` block to prevent shell injection attacks — backticks, `$()`, or other shell metacharacters in the PR body cannot execute as commands.

---

### `copilot-agents.yml` — Copilot-Inspired Quality Checks

**Triggers:**
- `pull_request` opened or synchronized
- `push` to `main`
- Weekly schedule: Mondays at 02:00 UTC

**Jobs:**

| Job | Trigger condition | Purpose |
|-----|------------------|---------|
| `code-review-agent` | PRs only | Scans for MVC structure (controllers, models, views) and checks for auth/HTTPS configuration patterns |
| `build-validator-agent` | All triggers | Validates that `Program.cs` and `appsettings.json` exist, then builds the project |
| `security-reviewer-agent` | PRs + weekly schedule | Runs `dotnet list package --vulnerable` and scans `appsettings*.json` for potential secrets |
| `documentation-helper-agent` | PRs + pushes to `main` | (Defined but checks documentation completeness) |

**Note:** These jobs run shell-based heuristic checks. They complement, but do not replace, the Copilot Chat agents in `.github/agents/` — those operate in your IDE or on GitHub.com as AI-powered assistants, while this workflow runs automated scripted checks in CI.

---

### `issue-triage-agent.yml` — Automatic Issue Triage

**Triggers:**
- `issues` opened

**Permissions:** `issues: write` (to add labels and post comments)

**What it does:**

1. **Ensures difficulty labels exist** — Creates or updates the `easy`, `moderate`, and `difficult` labels with consistent colors and descriptions using the GitHub API
2. **Classifies the issue** — Reads the issue title and body, matches against keyword lists:
   - **Difficult:** authentication, OAuth, Azure AD, Playwright, Docker, Kubernetes, telemetry, security, CI/CD, Roslyn analyzers
   - **Moderate:** health checks, logging, monitoring, Dockerfile, tests, formatters
   - **Easy:** everything else (bugs, typos, config tweaks, small UI changes)
3. **Applies the difficulty label** to the issue automatically
4. **Posts a triage comment** summarizing the classification, suggested next steps, and an estimate of effort

**This workflow fires for every new issue** — no manual action required. It is the automated counterpart to the `issue-helper.agent.md` custom Copilot agent.

---

## Reusable Workflow Pattern

`build-and-test.yml` is designed as a **reusable workflow** (`workflow_call`). It is called by both `dotnet.yml` and `deploy.yml`, ensuring build consistency:

```yaml
# Calling it from another workflow:
jobs:
  build:
    uses: ./.github/workflows/build-and-test.yml
    with:
      dotnet-version: "9.0.x"
      install-playwright: "true"
```

This avoids duplicating the build/test/cache/publish logic across multiple workflows.

---

## Workflow Dependency Map

```text
dotnet.yml ──────────────────────────────► build-and-test.yml (reusable)
  └─ on: push/PR to main                    └─ restore, build, test, publish

deploy.yml ──────────────────────────────► build-and-test.yml (reusable)
  └─ on: push to main or v* tag               └─ artifact: published-app
      └─► docker job (build & push image)

version.yml
  └─ on: PR merged to main
      └─► bump VERSION, tag, GitHub Release

copilot-agents.yml
  ├─ on: PR opened/sync ──────────────────► code-review-agent, security-reviewer-agent
  ├─ on: push to main ────────────────────► build-validator-agent
  └─ on: weekly schedule ─────────────────► security-reviewer-agent

pipeline-triage.md
  └─ on: issue comment `/triage` by `rbmathis` ─► classify, label, comment
```

## References

- [GitHub Docs: Understanding GitHub Actions](https://docs.github.com/en/actions/learn-github-actions/understanding-github-actions)
- [GitHub Docs: Reusing workflows](https://docs.github.com/en/actions/using-workflows/reusing-workflows)
- [GitHub Docs: Caching dependencies](https://docs.github.com/en/actions/using-workflows/caching-dependencies-to-speed-up-workflows)
- [GitHub Docs: Security hardening for GitHub Actions](https://docs.github.com/en/actions/security-guides/security-hardening-for-github-actions)
