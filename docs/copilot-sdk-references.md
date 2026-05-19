# Copilot SDK References

Cyberpilot uses the GitHub Copilot SDK as the agent runtime for SDK-mode pipeline stages. Read this reference before changing session orchestration, streaming, permissions, tools, hooks, model selection, or SDK persistence behavior.

## Upstream References

| Topic | Reference |
| --- | --- |
| Getting started | https://docs.github.com/en/copilot/how-tos/copilot-sdk/sdk-getting-started |
| SDK documentation index | https://github.com/github/copilot-sdk/blob/main/docs/index.md |
| First Copilot-powered app | https://github.com/github/copilot-sdk/blob/main/docs/getting-started.md |
| Agent loop | https://github.com/github/copilot-sdk/blob/main/docs/features/agent-loop.md |
| Streaming events | https://github.com/github/copilot-sdk/blob/main/docs/features/streaming-events.md |
| Steering and queueing | https://github.com/github/copilot-sdk/blob/main/docs/features/steering-and-queueing.md |
| Session persistence | https://github.com/github/copilot-sdk/blob/main/docs/features/session-persistence.md |
| Custom agents and sub-agent orchestration | https://github.com/github/copilot-sdk/blob/main/docs/features/custom-agents.md |
| Custom skills | https://github.com/github/copilot-sdk/blob/main/docs/features/skills.md |
| Model Context Protocol servers | https://github.com/github/copilot-sdk/blob/main/docs/features/mcp.md |
| Pre-tool use hook | https://github.com/github/copilot-sdk/blob/main/docs/hooks/pre-tool-use.md |
| Post-tool use hook | https://github.com/github/copilot-sdk/blob/main/docs/hooks/post-tool-use.md |
| OpenTelemetry instrumentation | https://github.com/github/copilot-sdk/blob/main/docs/observability/opentelemetry.md |

The SDK is in public preview. Verify current API names and event shapes against upstream docs or source before implementing non-trivial changes.

## Supported Harness Capabilities

Cyberpilot should design around these SDK capabilities:

- Explicit sessions with known lifecycle and cleanup.
- Per-session model selection and model fallback.
- Streaming session events for progress, usage, tools, errors, idle state, and completion.
- `assistant.usage` events for token, cache, duration, provider, and quota details.
- `assistant.turn_start` and `assistant.turn_end` events for model turn counting.
- `session.idle` as a mechanical completion signal.
- Session persistence and resume for crash recovery, pause, and human intervention.
- Steering and queueing for active-stage operator guidance.
- Custom tools for deterministic GitHub, git, validation, state, and policy operations.
- Pre-tool hooks for permissions, argument normalization, output suppression, and timeout defaults.
- Post-tool hooks for redaction, truncation, summarized output, audit trails, and failure hints.
- Custom agents and skills for scoped specialist behavior when they reduce complexity.
- Model Context Protocol (MCP) servers for mature, general-purpose integrations that are worth reusing.
- OpenTelemetry for stage/run/provider correlation.

## Cyberpilot Integration Notes

Current SDK-mode execution is stage-scoped. `CopilotStageRunner` creates a Copilot client and session per pipeline stage, enables streaming, streams assistant deltas to the progress sink, and captures final usage metrics as a fallback.

`PromptBuilder` currently renders a broad wrapper around each imported stage prompt. Future optimization should move deterministic run state into typed context and tools so stage prompts do not rediscover issue, pull request, branch, or repository facts that the harness already knows.

Pipeline state belongs to the Cyberpilot harness, not the model conversation. Use database-backed run, stage, evidence, and artifact records for canonical workflow state. Use issue and pull request comments as human-readable reports.

## SDK Harness Decisions

Record future SDK integration decisions in this section or link to a dated decision record.

| Area | Decision | Notes |
| --- | --- | --- |
| Session lifetime | Use one SDK session per stage by default, with stable run/stage/attempt session IDs once session persistence is implemented. | This preserves stage isolation while creating a path to resume and cleanup. |
| Review parallelization | Prototype harness-level parallel review sessions before custom agents inside one review session. | Separate sessions simplify read-only policy, metrics, timeout control, and partial-failure handling. |
| Streaming metrics | Prefer streaming `assistant.usage`, turn, tool, error, and idle events. Keep final RPC usage metrics as a fallback. | Metrics should make expensive stages and loops visible before behavior changes. |
| Tool placement | Put reusable deterministic Cyberpilot operations in the SDK project. Keep web-only orchestration in the web host. | The SDK is consumed by both the web app and console harness. |
| Tool policy | Start with least-privilege stage policies and deny writes unless a stage explicitly requires them. | Review and analysis stages should be read-only by default. |
| Tool output | Persist redacted raw output for humans with retention limits. Feed compact summaries to the model by default. | This keeps auditability without bloating model context. |
| Deterministic PR tools | Attach `get_pipeline_context`, `get_pr_details`, and `get_pr_diff_summary` to SDK stage sessions. | Tools return compact typed payloads and persist detailed JSON as artifact rows through the stage result path. |
| Tool hooks | Attach pre/post tool hooks to SDK stage sessions. | Pre hooks deny broad writes in read-only stages; post hooks redact/truncate output and persist shaped artifacts. |
| Model tiers | Support per-stage model overrides and fallback recording. | `--stage-model` and `--stage-fallback-model` feed `StageModelResolver`; selected/fallback metadata is stored on stage logs. |
| MCP | Keep GitHub, SQLite/database, and filesystem access native for now. | Native tools currently have better typed payloads, deterministic errors, mutation policy, audit trails, and lower operational surface. Revisit MCP only when a mature server clearly beats native tools. |
| Cleanup | Keep structured artifacts and metrics with run history. Retain raw tool output and resumable session records for a shorter configurable window. | Reset Mission should remove local session/artifact state unless delivery completed. |
