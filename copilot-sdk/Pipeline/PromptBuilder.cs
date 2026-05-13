namespace Cyberpilot.Pipeline;

internal interface IPromptBuilder
{
	Task<string> BuildAsync(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, CancellationToken cancellationToken = default);
}

internal sealed class PromptBuilder(string repoRoot, string agentPromptRoot, int issueNumber) : IPromptBuilder
{
	public async Task<string> BuildAsync(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, CancellationToken cancellationToken = default)
	{
		var stage = stageDefinition.Stage;
		var promptPath = Path.Combine(agentPromptRoot, ".github", "agents", stage.PromptFile);
		var stagePrompt = await File.ReadAllTextAsync(promptPath, cancellationToken);
		var requiredArtifacts = stageDefinition.Contract.RequiredArtifacts.Count == 0
			? "none"
			: string.Join(", ", stageDefinition.Contract.RequiredArtifacts.Select(artifact => $"`{artifact}`"));
		var artifactExample = BuildArtifactExample(stageDefinition.Contract.RequiredArtifacts);

		return $$"""
			You are running as the Cyberpilot SDK cyberpilot controller.

			Target issue: #{{issueNumber}}
			Repository root: {{repoRoot}}
			Agent prompt root: {{agentPromptRoot}}
			Stage: {{stage.Name}}
			Mission: {{mission}}
			Policy profile: {{policyProfile.Name}}
			Stage result contract version: {{stageDefinition.Contract.Version}}
			Required artifacts: {{requiredArtifacts}}

			The controller has already applied the permanent `sdk` provenance label and the correct SDK stage label for this stage.
			Do not manage the `sdk` label or any `sdk/*` labels yourself. Do not close the issue.

			Execute the stage instructions below yourself. Use the issue thread as the state file.
			Do not delegate to background agents or wait for specialist agent tasks. When the imported prompt asks for specialist input, perform that specialist analysis directly in this SDK session and include the result in the stage summary.

			## Output Formatting

			Structure your streaming output using markdown so the dashboard can render it clearly:
			- Use `## Section Title` headers to separate major phases of your work (e.g., `## Reading the Issue`, `## Analysis`, `## Building`)
			- Use bullet lists (`- item`) for key findings, changes, or decisions
			- Use **bold** for important terms, file paths, or status outcomes
			- Use tables when comparing options or listing structured data
			- Keep paragraphs short and punchy — your personality should shine through the structure
			- End with a clear summary section (e.g., `## Result` or `## Verdict`)

			At the very end of your response, include a fenced JSON block with the best available stage result. The JSON must include `contract_version` and every required artifact for this stage when you have enough information to produce artifacts:

			```json
			{
			  "status": "GO",
			  "decision": "approved",
			  "contract_version": "{{stageDefinition.Contract.Version}}",
			  "artifacts": {{artifactExample}},
			  "evidence": [
			    { "name": "summary", "summary": "brief evidence summary" }
			  ],
			  "policy_rationale": "why this result satisfies the {{policyProfile.Name}} policy profile",
			  "required_actions": [],
			  "issue_number": {{issueNumber}}
			}
			```

			Use these status values when applicable: GO, STOP, DUPLICATE.
			Use these review decision values when applicable: approved, changes_requested, comment.
			When status is STOP or the result needs human correction, populate `required_actions` with concrete next steps.

			<stage-agent-prompt>
			{{stagePrompt}}
			</stage-agent-prompt>
			""";
	}

	private static string BuildArtifactExample(IReadOnlyList<string> requiredArtifacts)
	{
		if (requiredArtifacts.Count == 0)
		{
			return "{}";
		}

		var entries = requiredArtifacts.Select(artifact => $"\"{artifact}\": \"brief artifact summary or URI\"");
		return $"{{ {string.Join(", ", entries)} }}";
	}
}
