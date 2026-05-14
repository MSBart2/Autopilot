# Policy Profiles

Cyberpilot policy profiles describe how conservative an SDK run should be when evaluating stage output, deterministic gates, and human-readable policy rationale. The selected profile is recorded on each new run, injected into SDK stage prompts, and included in structured stage results and evidence when stages report policy decisions.

## Available Profiles

| Profile | Strictness | Intended use |
|---------|------------|--------------|
| `lenient` | Lenient | Exploratory runs where diagnostics are useful but blocking should be minimal. |
| `standard` | Standard | Default balance for normal issue-to-PR work. |
| `strict` | Strict | Conservative runs where missing artifacts, validation gaps, or unclear evidence should block progress. |
| `security-critical` | SecurityCritical | Security-sensitive work that needs the strongest policy posture. |

The web launcher and SDK command line both default to `standard`.

## Selecting A Profile

In the SDK executable, pass `--policy-profile`:

```powershell
dotnet run --project .\copilot-sdk-exe\Cyberpilot.Sdk.Exe.csproj -- run issue 135 --repo rbmathis/Cyberpilot --approve-all --policy-profile strict
```

In the web launcher, choose the policy profile before starting the run. The selected profile is stored with the run and sent to the SDK runner.

Programmatic SDK callers can set `PolicyProfileName` on `CyberpilotRunRequest`.

## What Profiles Affect

Policy profiles currently affect these surfaces:

- Stage prompts receive the selected profile name and must explain `policy_rationale` in their final JSON result.
- Stage artifact validation checks the selected stage contract before routing accepts a result.
- Deterministic gates can block stages and return required corrective actions.
- Evidence rows capture policy rationale, gate outcomes, approval decisions, validation output, and stage artifacts.
- Run Room telemetry displays the selected policy profile for operational context.

Profiles are not a substitute for deterministic checks. Facts such as model availability, required labels, branch readiness, linked pull request presence, and review approval belong in gates. Agent stages provide judgment, synthesis, and explanation.

## Gates And Required Actions

Pipeline definitions can attach gates before or after stages. A failed blocking gate returns an invalid stage result with:

- gate evidence
- policy rationale
- required corrective actions
- a halted or retryable routing decision, depending on where the gate runs

When a stage itself returns `STOP` or cannot satisfy its contract, the final JSON result should include `required_actions` that an operator or issue author can complete.

## Compatibility

Older runs may not have policy profile metadata, structured stage-result JSON, or evidence rows. The Run Room falls back to the default profile where metadata is missing and should treat absent policy evidence as historical data, not as proof that checks passed or failed.