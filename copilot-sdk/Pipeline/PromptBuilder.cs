namespace Cyberpilot.Pipeline;

internal interface IPromptBuilder
{
	Task<string> BuildAsync(StageDefinition stage, string mission, CancellationToken cancellationToken = default);
}

internal sealed class PromptBuilder(string repoRoot, string agentPromptRoot, int issueNumber) : IPromptBuilder
{
	public async Task<string> BuildAsync(StageDefinition stage, string mission, CancellationToken cancellationToken = default)
	{
		var promptPath = Path.Combine(agentPromptRoot, ".github", "agents", stage.PromptFile);
		var stagePrompt = await File.ReadAllTextAsync(promptPath, cancellationToken);

		return $$"""
			You are running as the Cyberpilot SDK cyberpilot controller.

			Target issue: #{{issueNumber}}
			Repository root: {{repoRoot}}
			Agent prompt root: {{agentPromptRoot}}
			Stage: {{stage.Name}}
			Mission: {{mission}}

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

			At the very end of your response, include a fenced JSON block with the best available stage result:

			```json
			{
			  "status": "GO",
			  "decision": "approved",
			  "issue_number": {{issueNumber}}
			}
			```

			Use these status values when applicable: GO, STOP, DUPLICATE.
			Use these review decision values when applicable: approved, changes_requested, comment.

			<stage-agent-prompt>
			{{stagePrompt}}
			</stage-agent-prompt>
			""";
	}
}
