# Plan: Mature SDK SDLC Harness

Cyberpilot should evolve from a hardcoded six-stage runner into a typed, policy-aware SDLC harness while preserving the current `Triage -> Plan -> Implement -> Review -> Docs -> Deliver` pipeline as the default behavior.

The implementation should be incremental. First introduce process definitions and contracts as passive/defaulted concepts, then refactor routing to consume them, then add validation, deterministic gates, human approvals, evidence, UI, and docs.

## Steps

1. Baseline and guardrails
   - Confirm the current committed state builds and tests before changes.
   - Capture current six-stage behavior as compatibility requirements: same labels, same review loop cap, same `--skip-deliver`, same `--allow-missing-docs`, and same resume semantics.
   - Add characterization tests around `SdkCyberpilotRunner` stage order, review rework, docs skipping, deliver skipping, and resume from each stage before changing orchestration internals.

2. Define the default process model
   - Add SDK model types for `PipelineDefinition`, `PipelineStageDefinition`, `StageTransition`, `PolicyProfile`, `StageContract`, `GateDefinition`, and `PipelineDefinitionVersion`.
   - Implement an in-memory `DefaultPipelineDefinitionProvider` that returns the current six-stage Cyberpilot SDLC with the `standard` policy profile.
   - Keep `StageCatalog` as a compatibility shim initially, backed by the default definition.
   - Add unit tests proving the default definition exactly matches today's stage names, prompt files, labels, and order.

3. Version runs and stage contracts
   - Extend `CyberpilotRunRequest` and `CyberpilotOptions` with optional `PipelineDefinitionName`, `PipelineDefinitionVersion`, and `PolicyProfileName`, defaulting to the current Cyberpilot definition.
   - Extend persistence with nullable run-level fields for `PipelineDefinitionName`, `PipelineDefinitionVersion`, `PolicyProfileName`, and `ContractVersion`.
   - Add migration and tests proving old runs remain readable and new runs persist definition metadata.
   - Surface this metadata in run history/details as operational context, not a major UI redesign yet.

4. Refactor routing behind a pipeline engine
   - Extract orchestration from `SdkCyberpilotRunner.RunPipelineAsync` into a service such as `PipelineEngine` or `IPipelineRouter`.
   - Introduce `PipelineExecutionContext` carrying run request, definition, current stage, branch/PR state, policy profile, and accumulated results.
   - Port the existing hardcoded flow into definition-driven execution while preserving special cases: duplicate triage completes, review can rework implement, docs can be waived with `AllowMissingDocs`, and deliver can be skipped.
   - Keep labels and progress sink events identical where possible to avoid breaking the web UI.
   - Add SDK tests for every existing routing behavior against the new engine.

5. Persist richer stage results
   - Add nullable `StageResultJson`, `StageResultContractVersion`, and optional `RetryReason` to `PipelineStageLog`.
   - Persist serialized `StageResult` from both history and SignalR progress sinks.
   - Extend `RetryStageRequest` and retry/rework endpoints to capture an operator-provided reason where applicable.
   - Update Run Room stage cards to show structured result status/decision, invalid-result errors, retry reason, token/cost, and attempt count without changing the stage transcript layout.
   - Add model, controller, and sink tests for stage result persistence and retry reason display.

6. Introduce artifact contracts and validation
   - Extend `StageResult` with `ContractVersion`, `Artifacts`, `Evidence`, `PolicyRationale`, and `RequiredActions` in a backward-compatible way.
   - Add an `IStageArtifactValidator` abstraction that validates the structured result after a stage completes and before routing decisions are accepted.
   - Start with lightweight validation in code for required fields per stage rather than taking a large dependency immediately; JSON Schema can follow if needed.
   - Fail closed for required artifacts under `standard` and `strict`, but provide clear diagnostics and corrective actions.
   - Update stage prompts to include the selected contract version and required artifact shape.

7. Add policy profiles and deterministic gates
   - Add built-in profiles: `lenient`, `standard`, `strict`, and `security-critical`.
   - Add `IPipelineGate` and `PipelineGateResult` abstractions for deterministic checks.
   - Implement first gates around existing truths: model availability, required labels, branch writability, build/test command success, coverage threshold, docs requirement, PR presence, and review approval.
   - Persist gate outcomes as structured evidence, initially through stage result JSON or a new evidence table depending on migration appetite.
   - Route failures by gate type: retryable gate failures return to the relevant stage; policy-blocking failures pause or stop with corrective actions.

8. Make human approval first-class
   - Add approval state types such as `ApprovalGateRequest`, `ApprovalDecision`, and `ApprovalStatus`.
   - Add persistence for pending approval gates, either as new `PipelineApproval` rows or structured dispatch/evidence records if starting smaller.
   - Add web endpoints for approve/reject/resume, guarded so completed delivered runs cannot be altered.
   - Update `ShouldPauseAsync` into a richer pause decision mechanism that can request approval before or after named stages.
   - Render pending approval gates in the Run Room with reason, requested role, created time, approve/reject actions, and resume target.

9. Build the evidence ledger
   - Add a durable evidence model for stage artifacts, deterministic gate results, validation output, approvals, PR/commit references, model/cost usage, and policy decisions.
   - Keep raw transcripts in `PipelineStageLog.Output`, but store summarized structured evidence separately for filtering and reporting.
   - Update final deliver/landing report generation to include evidence links and policy summary.
   - Add UI sections for Evidence, Policy, and Approvals that are compact and scan-friendly.

10. Enable multiple definitions and target-repo profiling
    - Add a file-backed or configuration-backed `PipelineDefinitionProvider` after the default typed model is stable.
    - Support process variants such as `bugfix`, `security`, `docs-only`, `hotfix`, and `experiment`.
    - Add an optional `RepoProfile` preflight stage that detects build/test/doc conventions for target repositories controlled by Cyberpilot.
    - Allow the web launcher and CLI to select a definition/profile while defaulting to the existing Cyberpilot SDLC.

11. Update prompts and docs progressively
    - Update `.github/agents/*.agent.md` only after the SDK can pass policy/contract context into prompts.
    - Start with stage agents that already produce structured decisions: triage, review, docs, and deliver.
    - Update `AI-SDLC.md`, `architecture.md`, `docs/configuration.md`, `docs/testing.md`, and add `docs/policies.md` plus `docs/approval-workflow.md`.
    - Document compatibility rules: old runs may not have policy/evidence data; new runs record definition/profile/contract version.

## Verification Strategy

- For each phase, run `dotnet build .\Cyberpilot.sln`.
- After SDK changes, run `dotnet test .\tests\Cyberpilot.Sdk.Tests\Cyberpilot.Sdk.Tests.csproj --no-build`.
- After web or persistence changes, run focused web unit tests, then integration tests if routes or migrations change.
- For Run Room UI changes, use a seeded/completed run plus an active local smoke run to verify historical replay and live SignalR updates.

## Decisions

- Implement this as small shippable slices, not one broad architecture drop.
- First implementation slice: default `PipelineDefinition` plus compatibility/characterization tests, with no UI or prompt changes yet.
- Preserve the current six-stage Cyberpilot SDLC as the default definition; do not force users to choose a definition for existing workflows.
- Keep pipeline definitions code-first initially for type safety; add JSON/YAML/config providers after the model settles.
- Make policy profiles advisory at first, then move selected gates to blocking once diagnostics and behavior are trusted.
- Prefer additive nullable persistence fields for early metadata; introduce normalized approval/evidence tables when human gates and evidence search need them.
- Keep policy/contract enforcement inside the SDK harness, not in prompts alone.
- Use deterministic gates for facts and agent stages for judgment/synthesis.
- Model deterministic gates as attached pre/post gates around stages, not normal prompt-backed stages.
- Treat human approval as a resumable pipeline state, not as a failed run.
- Implement the first human approval UX in the web Run Room while keeping the SDK state model generic enough for CLI later.
- Avoid prompt churn in phase one; update prompts only after the SDK can inject policy/contract context.
- Version the pipeline definition and stage result contract on every new run.
- Use `docs-only` as the first low-risk process variant after the default pipeline proves stable.

## First Slice

The first slice is intentionally small:

1. Add the default code-first pipeline definition model.
2. Back `StageCatalog` with that default definition.
3. Add tests proving no stage metadata or ordering changed.
4. Build and run SDK tests.

No routing changes, prompt changes, web UI changes, or persistence migrations should be included in the first slice.

### First Slice Status

Started in this branch:

- Added code-first default pipeline definition types and provider.
- Backed `StageCatalog` with the default definition while preserving its existing API.
- Added SDK tests proving the default definition preserves stage identity, order, prompt files, labels, policy profile, and review transitions.
- Validated with `dotnet build .\Cyberpilot.sln` and `dotnet test .\tests\Cyberpilot.Sdk.Tests\Cyberpilot.Sdk.Tests.csproj --no-build`.

### Second Slice Status

Started after the default definition foundation:

- Added passive pipeline definition/profile metadata to `CyberpilotRunRequest` and `CyberpilotOptions`.
- Added CLI parsing for `--pipeline-definition`, `--pipeline-version`, and `--policy-profile`.
- Wired request metadata through `CyberpilotRunner` into SDK options without changing routing behavior.
- Added SDK option tests for default metadata, explicit metadata, help text, and empty-value validation.
- Validated with `dotnet build .\Cyberpilot.sln` and `dotnet test .\tests\Cyberpilot.Sdk.Tests\Cyberpilot.Sdk.Tests.csproj --no-build`.

### Third Slice Status

Persisted run metadata for future routing and UI display:

- Added public default pipeline metadata constants while keeping the full default definition model internal.
- Added nullable pipeline definition, definition version, policy profile, and contract version columns to `PipelineRun`.
- Generated the EF Core migration `AddPipelineDefinitionMetadataToRun`.
- Stamped new web and SDK-exe runs with the default definition/profile/contract metadata.
- Carried persisted metadata through queued web run requests into `CyberpilotRunRequest`.
- Added SDK persistence and web queue handoff tests.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Fourth Slice Status

Surfaced definition metadata in the Run Room:

- Added Run Room view-model labels for pipeline definition, policy profile, and contract version.
- Displayed the definition/profile/contract metadata in the existing telemetry grid.
- Added view-model tests for stored metadata and fallback default metadata.
- Stabilized the web pipeline service tests by moving their SQLite fixture to a per-test database file, avoiding shared-connection races while the background service is active.
- Validated with `dotnet build .\Cyberpilot.sln` and web unit tests.

### Fifth Slice Status

Added routing characterization tests before extracting a pipeline engine:

- Covered resume from every stage.
- Covered docs failure with and without `AllowMissingDocs`.
- Covered review change requests exhausting the two-cycle rework loop.
- Extracted current stage orchestration into `PipelineEngine` with `PipelineExecutionContext` while preserving `SdkCyberpilotRunner` preflight behavior and public run result fields.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Sixth Slice Status

Moved the extracted engine toward definition-driven stage metadata while preserving current behavior:

- Added a definition-aware `PipelineStartResolver` overload.
- Updated `PipelineEngine` to resolve start stages and stage metadata from `PipelineExecutionContext.Definition`.
- Kept `StageCatalog` compatibility in place for existing callers and index semantics.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Seventh Slice Status

Removed remaining runtime routing dependencies on `StageCatalog` from the extracted engine path:

- Added definition stage lookup helpers for stage retrieval, index lookup, and `ShouldRun` checks.
- Updated existing-PR fast-forward routing to resolve Review from the selected definition.
- Removed `StageCatalog` index usage from `PipelineStart` and engine routing decisions.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Eighth Slice Status

Validated selected pipeline definition/profile before routing starts:

- Added `PipelineDefinitionSelector` for the current built-in definition registry.
- Accepted only `cyberpilot-default` version `1.0` with the `standard` policy profile until additional definitions exist.
- Added clear unsupported-definition/profile diagnostics with exit code `12` before issue, label, model, or stage side effects.
- Added selector and runner tests for supported and unsupported selections.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Ninth Slice Status

Made declared pipeline transitions queryable and used them for review routing decisions:

- Added transition lookup helpers for condition-based routing.
- Used the default definition's `review -> implement` transition for review rework.
- Used the default definition's `review -> docs` transition after approval.
- Added transition lookup tests, including missing-transition diagnostics.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Tenth Slice Status

Validated selected pipeline definitions before routing starts:

- Added `PipelineDefinitionValidator` for definition identity, stage metadata, contract versions, duplicates, and transition endpoints.
- Wired validation into `PipelineDefinitionSelector` so invalid selected definitions fail before routing.
- Added tests for the valid default definition and actionable invalid-definition errors.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Eleventh Slice Status

Added passive persistence fields for richer structured stage results:

- Added nullable `StageResultJson`, `StageResultContractVersion`, and `RetryReason` fields to `PipelineStageLog`.
- Generated the EF Core migration `AddStructuredStageResultMetadata`.
- Added SDK persistence tests proving defaults remain null and structured result metadata round-trips.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twelfth Slice Status

Persisted serialized stage results from progress sinks:

- Updated SDK history progress sink to store `StageResultJson` and `StageResultContractVersion` when a stage completes.
- Updated web SignalR progress sink to store the same structured metadata while preserving existing live events.
- Added SDK and web sink tests proving structured result metadata is persisted.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Thirteenth Slice Status

Captured operator retry reasons for web retry/rework attempts:

- Added queue plumbing so RetryStage preserves the operator-provided reason and Rework from Review records a fixed review-feedback reason.
- Persisted retry reasons on the matching stage log through the SignalR progress sink.
- Included retry reasons in live `stageStarted` events and historical Run Room log data.
- Displayed retry reasons on Run Room stage cards without changing the transcript layout.
- Added controller and SignalR sink tests for retry reason queueing, persistence, and live event payloads.
- Validated with `dotnet build .\Cyberpilot.sln` and web unit tests.

### Fourteenth Slice Status

Added backward-compatible stage result contract/artifact/evidence fields:

- Extended `StageResult` with optional contract version, artifacts, evidence, policy rationale, and required actions.
- Parsed those optional fields from stage JSON using both camelCase and snake_case names where relevant.
- Preserved existing constructor calls and defaulted empty results to the current contract version.
- Updated SDK and web progress sinks to persist the result's contract version, falling back to the default when missing.
- Added SDK tests for structured field parsing and SDK/web sink tests for result-specific contract version persistence.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Fifteenth Slice Status

Added artifact validation before routing decisions:

- Added `IStageArtifactValidator`, `StageArtifactValidationResult`, and `DefaultStageArtifactValidator`.
- Validated stage result contract versions against the selected stage contract.
- Added legacy-safe artifact checks: omitted artifacts remain compatible during prompt transition, while partial artifact payloads must include all declared required artifacts.
- Updated `StageExecutor` to validate successful stage results before emitting completion and before routing consumes them.
- Added validator unit tests and runner-level coverage proving missing declared artifacts halt before the next stage.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Sixteenth Slice Status

Injected selected stage result contracts into SDK prompts:

- Updated `IPromptBuilder` to receive the full `PipelineStageDefinition` and selected `PolicyProfile`.
- Added generated prompt guidance for policy profile, contract version, required artifacts, evidence, policy rationale, and required actions.
- Built the artifact JSON example from the selected stage contract so prompt guidance follows the pipeline definition.
- Updated `StageExecutor` and test fakes to use the richer prompt-builder contract.
- Added prompt-builder tests for required artifact guidance and policy/action fields.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Seventeenth Slice Status

Added built-in policy profile selection:

- Added built-in `lenient`, `standard`, `strict`, and `security-critical` policy profiles.
- Updated the pipeline definition selector to apply the selected built-in profile to the default definition.
- Kept unsupported definitions and versions rejected before side effects.
- Updated SDK help text to advertise available profiles.
- Added selector and option-help tests for the built-in profiles.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Eighteenth Slice Status

Added deterministic gate primitives and passive engine integration:

- Added `IPipelineGate`, `PipelineGateContext`, `PipelineGateResult`, `PipelineGateEvaluation`, and `PipelineGateRunner`.
- Added a `gate` dispatch type for deterministic policy checks.
- Wired `PipelineEngine` to run declared pre/post stage gates before accepting routing decisions.
- Kept default behavior unchanged by using an empty gate registry while no default gates are declared.
- Added gate runner tests for empty definitions, matching gate execution, and missing evaluator diagnostics.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Nineteenth Slice Status

Added the first concrete deterministic gate for model availability:

- Added `BuiltInPipelineGates` with the `model-available` gate registration.
- Added `ModelAvailabilityGate` backed by the existing `IModelAvailabilityChecker`.
- Returned actionable corrective guidance when the selected model is unavailable.
- Registered built-in gates with the pipeline engine while preserving current behavior because no default gates are declared yet.
- Added model availability gate tests for pass/fail behavior and built-in registration.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twentieth Slice Status

Added a deterministic gate for required SDK labels:

- Added `RequiredLabelsGate` backed by the existing `ISdkLabelService`.
- Registered the `required-labels` gate in `BuiltInPipelineGates` alongside `model-available`.
- Preserved current default behavior because no default stages declare gates yet.
- Added pass/fail/create-missing tests and built-in registration coverage.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twenty-First Slice Status

Added a deterministic gate for linked pull request presence:

- Added `PullRequestPresenceGate` backed by `IGitHubIssueClient.FindPullRequestForIssueAsync`.
- Registered the `pull-request-present` gate in `BuiltInPipelineGates`.
- Returned retryable corrective actions when no linked PR exists or the linked PR is not open.
- Preserved current default behavior because no default stages declare gates yet.
- Added pass/fail/closed-PR tests and built-in registration coverage.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twenty-Second Slice Status

Added post-stage gate result context and a review approval gate:

- Extended `PipelineGateContext` so post-stage gates can inspect the completed `StageResult`.
- Updated `PipelineGateRunner` and `PipelineEngine` to pass the stage result into after-stage gates.
- Added the `review-approved` built-in gate for deterministic review approval checks.
- Returned retryable corrective actions when review requests changes.
- Added gate runner coverage for stage-result propagation and review approval gate tests.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twenty-Third Slice Status

Added a deterministic gate for branch readiness:

- Added the `branch-ready` built-in gate to validate that `PipelineExecutionContext.BranchName` is populated.
- Returned retryable corrective guidance when no pipeline branch is available.
- Registered the gate in `BuiltInPipelineGates` while keeping default definitions unattached.
- Added branch-ready gate tests for pass, missing branch, blank branch, and built-in registration coverage.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twenty-Fourth Slice Status

Persisted blocking gate outcomes as structured stage-result evidence:

- Enriched blocking gate failure `StageResult` values with gate evidence and policy rationale.
- Preserved existing dispatch logging while adding machine-readable gate context to halted results.
- Carried gate corrective actions into the synthetic invalid stage result.
- Added engine-level coverage proving a blocking gate halts before agent execution and records evidence/actions.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twenty-Fifth Slice Status

Added first-class approval request and decision state primitives:

- Added `ApprovalGateRequest` as the SDK model for pending human approval gates.
- Added `ApprovalDecision` and `ApprovalStatus` for approved, rejected, cancelled, and pending states.
- Added immutable approve/reject transitions with actor validation and trimmed decision metadata.
- Added SDK tests for pending state, approval, rejection, duplicate decisions, and missing actors.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twenty-Sixth Slice Status

Added a richer pause decision hook for approval-aware pauses:

- Added `PipelinePauseContext` and `PipelinePauseDecision` for structured pause decisions.
- Added an optional `ShouldPauseDecisionAsync` hook to SDK options while preserving the existing boolean pause callback.
- Updated `PipelineEngine` to prefer structured pause decisions and emit approval dispatch events when an approval request is attached.
- Added pause decision unit tests and runner coverage for approval-aware pauses after plan.
- Validated with `dotnet build .\Cyberpilot.sln` and SDK tests.

### Twenty-Seventh Slice Status

Added durable persistence for pending pipeline approvals:

- Added `PipelineApproval` as a run-owned persistence entity for human approval requests and decisions.
- Added `PipelineApprovals` to `CyberpilotDbContext` with run, status, and stage indexes plus cascade delete from runs.
- Generated the EF Core migration `AddPipelineApprovals` and refreshed the model snapshot.
- Added persistence tests for approval round-trip storage and cascade deletion.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Twenty-Eighth Slice Status

Mapped approval gate requests into durable approval rows:

- Added `PipelineApproval.FromRequest` to translate `ApprovalGateRequest` into persisted approval rows.
- Preserved pending approval metadata including timing, reason, role, resume stage, and creation time.
- Persisted approved/rejected decision metadata including actor, reason, status, and decision time.
- Added SDK persistence tests for pending, approved, and rejected approval request mappings.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Twenty-Ninth Slice Status

Surfaced persisted approvals in pipeline details view models:

- Loaded `PipelineApprovals` in the pipeline details action alongside logs and dispatches.
- Added `PipelineApprovalViewModel` for display-ready approval state, role, reason, timing, resume stage, and decision metadata.
- Added pending-first approval ordering and `HasPendingApprovals` on `PipelineRunDetailsViewModel`.
- Added model and controller tests proving details pages carry pending approvals.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Thirtieth Slice Status

Rendered persisted approval requests in the Run Room details page:

- Added a compact read-only Approvals panel to the Run Room details view.
- Displayed pending/resolved approval state, stage timing, reason, requested role, resume stage, request time, and decision metadata.
- Added responsive approval-panel styling that fits the existing operational Run Room layout.
- Kept approve/reject actions out of this slice until endpoints are wired.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Thirty-First Slice Status

Added guarded approve/reject endpoints for pending pipeline approvals:

- Added `PipelineApprovalDecisionRequest` for optional operator decision notes.
- Added `ApproveApproval` and `RejectApproval` POST endpoints with CSRF protection.
- Guarded delivered runs from approval mutation and prevented duplicate decisions on resolved approvals.
- Recorded decision status, actor, reason, and timestamp on pending approvals.
- Added controller tests for approve, reject, delivered-run guard, duplicate-decision guard, and missing approvals.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Thirty-Second Slice Status

Wired Run Room approval cards to approve/reject endpoints:

- Added pending approval decision forms to the Run Room approval panel.
- Posted approve/reject decisions to the guarded controller endpoints with anti-forgery tokens.
- Included optional decision notes for both approval and rejection paths.
- Kept resolved approvals read-only with their recorded decision metadata.
- Validated with `dotnet build .\Cyberpilot.sln` and web unit tests.

### Thirty-Third Slice Status

Added approved-approval resume from the Run Room:

- Added `ResumeApproval` to requeue a run from an approved approval's resume stage.
- Guarded active runs, delivered runs, non-resumable statuses, non-approved approvals, unknown resume stages, and competing active runs.
- Added a Resume button to approved approval cards when the run can continue.
- Passed an approval-specific retry reason into the queued web run request.
- Added controller tests for successful approval resume plus pending, delivered, and active-run guard paths.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Thirty-Fourth Slice Status

Completed approval-request progress sink persistence:

- Added `ICyberpilotProgressSink.OnApprovalRequested` so structured approval requests are first-class progress events.
- Promoted approval request primitives needed by the public sink contract.
- The pipeline engine now emits approval requests when `ShouldPauseDecisionAsync` returns a structured approval pause.
- `CyberpilotRunHistoryProgressSink` and `SignalRProgressSink` persist pending `PipelineApproval` rows, with SignalR also notifying clients via `approvalRequested`.
- `CompositeProgressSink` forwards approval requests and `TextWriterProgressSink` reports them in CLI output.
- Added SDK/web tests for sink persistence, composite forwarding, and engine-level approval request emission.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Thirty-Fifth Slice Status

Completed web-triggered structured pause approvals:

- Exposed `ShouldPauseDecisionAsync` on the public `CyberpilotRunRequest` and mapped it through `CyberpilotRunner` into SDK options.
- Promoted `PipelinePauseContext` and `PipelinePauseDecision` as documented public SDK types.
- Web background runs now use structured pause decisions; when a run is marked `Pausing`, the SDK receives an operator approval request that resumes at the next stage.
- The approval request flows through `SignalRProgressSink.OnApprovalRequested`, so paused web runs now create durable pending approvals automatically.
- Blocked the normal `Continue` route and hidden Continue affordance while approvals are pending, so approvals cannot be bypassed.
- Added web service/controller tests for structured pause decision wiring and pending approval Continue guards.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, web unit tests, and full solution tests.

### Thirty-Sixth Slice Status

Completed rejected approval handling:

- Rejected approval decisions now move the run to `Stopped`, set the current stage to the approval stage, and write a diagnostic error that includes the rejection reason when provided.
- The normal `Continue` route now blocks runs with rejected approvals so a rejected gate cannot be bypassed accidentally.
- `PipelineRunDetailsViewModel` now tracks rejected approvals and hides Continue when any are present.
- Added controller and view-model tests for rejection status, diagnostics, and Continue blocking.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, and web unit tests.

### Thirty-Seventh Slice Status

Started the durable evidence ledger:

- Added `PipelineEvidence` as a generic evidence table for stage evidence, artifacts, policy rationale, and required actions.
- Related evidence rows to `PipelineRun` and optionally to `PipelineStageLog`, with run cascade delete and nullable stage-log cleanup.
- Generated the EF Core migration `AddPipelineEvidenceLedger` and updated the model snapshot.
- `CyberpilotRunHistoryProgressSink` and `SignalRProgressSink` now persist structured evidence rows from completed `StageResult` payloads.
- Added SDK persistence tests, SDK sink tests, and web SignalR sink tests for evidence row creation.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, web unit tests, and web integration tests.

### Thirty-Eighth Slice Status

Surfaced the evidence ledger in the Run Room:

- Details now loads persisted `PipelineEvidence` rows alongside logs, dispatches, and approvals.
- Added compact `PipelineEvidenceViewModel` display items with stage labels, kind labels, optional media type, and evidence links.
- Rendered a restrained Evidence panel in the Run Room when ledger rows exist, without changing the stage transcript layout.
- Added controller and view-model tests proving evidence rows are loaded, sorted, and displayed through the details model.
- Validated with `dotnet build .\Cyberpilot.sln`, web unit tests, and web integration tests.

### Thirty-Ninth Slice Status

Added approval events to the evidence ledger:

- `PipelineEvidence` now creates ledger rows for approval requests and approval decisions.
- SDK history and web SignalR progress sinks persist approval-request evidence when structured approval gates are raised.
- Approval approve/reject decisions now write decision evidence rows, including actor and decision reason when provided.
- The Run Room Evidence panel labels approval request and decision entries clearly.
- Added SDK persistence/sink tests plus web controller, SignalR sink, and view-model tests for approval evidence.
- Validated with `dotnet build .\Cyberpilot.sln`, SDK tests, web unit tests, and web integration tests.
