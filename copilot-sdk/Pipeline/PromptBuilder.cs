namespace Cyberpilot.Pipeline;

/// <summary>The result of building a stage prompt, optionally split between a system message and user message.</summary>
/// <param name="UserMessage">The user-facing prompt containing runtime context, harness context, and the stage agent prompt.</param>
/// <param name="SystemMessageContent">When non-null, harness law content to inject via <c>SessionConfig.SystemMessage</c>.</param>
/// <param name="SystemMessageMode">The SDK system message mode to use when <see cref="SystemMessageContent"/> is non-null.</param>
internal sealed record BuiltPrompt(string UserMessage, string? SystemMessageContent, HarnessSystemMessageMode SystemMessageMode = HarnessSystemMessageMode.None);

internal interface IPromptBuilder
{
	Task<BuiltPrompt> BuildAsync(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, PipelineExecutionContext? context = null, CancellationToken cancellationToken = default);
}

internal sealed class PromptBuilder(
	string repoRoot,
	string agentPromptRoot,
	int issueNumber,
	string? targetRepositoryProfileSummary = null,
	CyberpilotRuntimePreferences? runtimePreferences = null) : IPromptBuilder
{
	public async Task<BuiltPrompt> BuildAsync(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, PipelineExecutionContext? context = null, CancellationToken cancellationToken = default)
	{
		var stage = stageDefinition.Stage;
		var promptPath = Path.Combine(agentPromptRoot, ".github", "agents", stage.PromptFile);
		var stagePrompt = await File.ReadAllTextAsync(promptPath, cancellationToken);
		var requiredArtifacts = stageDefinition.Contract.RequiredArtifacts.Count == 0
			? "none"
			: string.Join(", ", stageDefinition.Contract.RequiredArtifacts.Select(artifact => $"`{artifact}`"));
		var artifactExample = BuildArtifactExample(stageDefinition.Contract.RequiredArtifacts);
		var reportingGuidance = BuildReportingGuidance(stage.Name);
		var repositoryProfileContext = BuildRepositoryProfileContext(targetRepositoryProfileSummary);
		var harnessContext = BuildHarnessContext(stage.Name, context);
		var stageToolGuidance = BuildStageToolGuidance(stage.Name);
		var commandGuidance = BuildCommandGuidance(runtimePreferences);
		var systemMessage = runtimePreferences?.GetSystemMessageForStage(stage.Name)
			?? new HarnessStageSystemMessage();

		if (systemMessage.Mode is HarnessSystemMessageMode.Append or HarnessSystemMessageMode.Replace)
		{
			var mode = systemMessage.Mode;
			var systemContent = BuildHarnessSystemMessage(systemMessage.Profile, commandGuidance);
			var userContent = BuildUserMessage(stageDefinition, mission, policyProfile, stage, harnessContext, stageToolGuidance, requiredArtifacts, artifactExample, reportingGuidance, repositoryProfileContext, stagePrompt);
			return new BuiltPrompt(userContent, systemContent, mode);
		}

		return new BuiltPrompt(BuildFullPrompt(stageDefinition, mission, policyProfile, stage, harnessContext, stageToolGuidance, requiredArtifacts, artifactExample, reportingGuidance, repositoryProfileContext, commandGuidance, stagePrompt), null, HarnessSystemMessageMode.None);
	}

	private string BuildUserMessage(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, StageDefinition stage, string harnessContext, string stageToolGuidance, string requiredArtifacts, string artifactExample, string reportingGuidance, string repositoryProfileContext, string stagePrompt)
	{
		return $$"""
			Target issue: #{{issueNumber}}
			Repository root: {{repoRoot}}
			Agent prompt root: {{agentPromptRoot}}
			Stage: {{stage.Name}}
			Mission: {{mission}}
			Policy profile: {{policyProfile.Name}}
			Stage result contract version: {{stageDefinition.Contract.Version}}
			Required artifacts: {{requiredArtifacts}}
			{{harnessContext}}
			{{stageToolGuidance}}

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
			  "recommended_model_tier": "small|medium|large",
			  "issue_number": {{issueNumber}}
			}
			```

			Use these status values when applicable: GO, STOP, DUPLICATE.
			Use these review decision values when applicable: approved, changes_requested, comment.
			When status is STOP or the result needs human correction, populate `required_actions` with concrete next steps.
			Set `recommended_model_tier` only when downstream stages should spend more or less reasoning capacity based on the complexity or risk you found. Use `small`, `medium`, or `large`.
			{{repositoryProfileContext}}
			{{reportingGuidance}}

			<stage-agent-prompt>
			{{stagePrompt}}
			</stage-agent-prompt>
			""";
	}

	private string BuildFullPrompt(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, StageDefinition stage, string harnessContext, string stageToolGuidance, string requiredArtifacts, string artifactExample, string reportingGuidance, string repositoryProfileContext, string commandGuidance, string stagePrompt)
	{
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
			{{harnessContext}}
			{{stageToolGuidance}}

			The controller has already applied the permanent `sdk` provenance label and the correct SDK stage label for this stage.
			Do not manage the `sdk` label or any `sdk/*` labels yourself. Do not close the issue.

			Execute the stage instructions below yourself. Treat the harness context and structured artifacts as the primary workflow state. Use issue and pull request comments as human-readable reports, not as the canonical state store.
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
			  "recommended_model_tier": "small|medium|large",
			  "issue_number": {{issueNumber}}
			}
			```

			Use these status values when applicable: GO, STOP, DUPLICATE.
			Use these review decision values when applicable: approved, changes_requested, comment.
			When status is STOP or the result needs human correction, populate `required_actions` with concrete next steps.
			Set `recommended_model_tier` only when downstream stages should spend more or less reasoning capacity based on the complexity or risk you found. Use `small`, `medium`, or `large`.
			The final fenced `json` block must contain exactly one valid JSON object. Do not put prose, markdown, or another fenced block inside the final JSON fence.
			If an artifact contains markdown, store it as a normal JSON string: escape line breaks as `\n`, escape double quotes, and never paste raw multi-line markdown directly into the JSON block.
			Do not include nested triple-backtick fences inside artifact strings. If you must quote code in an artifact, use indented code blocks or short inline snippets instead.
			After the final fenced `json` block, do not write any additional text.
			{{commandGuidance}}
			{{repositoryProfileContext}}
			{{reportingGuidance}}

			<stage-agent-prompt>
			{{stagePrompt}}
			</stage-agent-prompt>
			""";
	}

	private static string BuildHarnessSystemMessage(HarnessSystemMessageProfile profile, string commandGuidance)
	{
		return profile switch
		{
			HarnessSystemMessageProfile.Lean => BuildLeanHarnessSystemMessage(commandGuidance),
			_ => BuildFullHarnessSystemMessage(commandGuidance),
		};
	}

	private static string BuildFullHarnessSystemMessage(string commandGuidance)
	{
		return $$"""
			You are running as the Cyberpilot SDK cyberpilot controller.

			The controller has already applied the permanent `sdk` provenance label and the correct SDK stage label for this stage.
			Do not manage the `sdk` label or any `sdk/*` labels yourself. Do not close the issue.

			Execute the stage instructions below yourself. Treat the harness context and structured artifacts as the primary workflow state. Use issue and pull request comments as human-readable reports, not as the canonical state store.
			Do not delegate to background agents or wait for specialist agent tasks. When the imported prompt asks for specialist input, perform that specialist analysis directly in this SDK session and include the result in the stage summary.

			## Output Formatting

			Structure your streaming output using markdown so the dashboard can render it clearly:
			- Use `## Section Title` headers to separate major phases of your work (e.g., `## Reading the Issue`, `## Analysis`, `## Building`)
			- Use bullet lists (`- item`) for key findings, changes, or decisions
			- Use **bold** for important terms, file paths, or status outcomes
			- Use tables when comparing options or listing structured data
			- Keep paragraphs short and punchy — your personality should shine through the structure
			- End with a clear summary section (e.g., `## Result` or `## Verdict`)

			## JSON Output Safety

			The final fenced `json` block must contain exactly one valid JSON object. Do not put prose, markdown, or another fenced block inside the final JSON fence.
			If an artifact contains markdown, store it as a normal JSON string: escape line breaks as `\n`, escape double quotes, and never paste raw multi-line markdown directly into the JSON block.
			Do not include nested triple-backtick fences inside artifact strings. If you must quote code in an artifact, use indented code blocks or short inline snippets instead.
			After the final fenced `json` block, do not write any additional text.
			{{commandGuidance}}
			""";
	}

	private static string BuildLeanHarnessSystemMessage(string commandGuidance)
	{
		return $$"""
			You are the Cyberpilot SDK controller for this stage.

			Run the stage yourself. Do not delegate to background agents, manage `sdk` labels, or close the issue. Treat harness context and structured artifacts as canonical workflow state; comments are only human-readable reports.

			End with exactly one fenced `json` block containing a valid stage result object. Include `contract_version`, required artifacts when available, evidence, policy rationale, required actions, optional `recommended_model_tier` (`small`, `medium`, or `large`) when downstream stages should adjust reasoning capacity, and `issue_number`. Do not write anything after the final JSON fence.
			{{commandGuidance}}
			""";
	}

	private static string BuildHarnessContext(string stageName, PipelineExecutionContext? context)
	{
		if (context is null)
		{
			return string.Empty;
		}

		var lines = new List<string>
		{
			"",
			"## Harness Context",
			"```json",
			context.CreateStageContext(stageName).ToCompactJson(),
			"```",
		};

		return string.Join(Environment.NewLine, lines);
	}

	private static string BuildStageToolGuidance(string stageName)
	{
		if (!stageName.Equals("review", StringComparison.OrdinalIgnoreCase)
		    && !stageName.Equals("docs", StringComparison.OrdinalIgnoreCase)
		    && !stageName.Equals("deliver", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		return """

			## Deterministic PR Tools

			Before using shell, GitHub commands, or subagents to discover pull request shape, call Cyberpilot's deterministic PR tools in this order:
			1. `get_pipeline_context`
			2. `get_pr_details`
			3. `get_pr_diff_summary`

			Treat `get_pr_diff_summary` as the authoritative changed-file map for this stage. Use its file list, top-directory groups, extension groups, and review signals to decide which files need deeper inspection. When inspecting files directly, call `get_changed_file_content` with repository-relative paths from the diff summary instead of using absolute local paths.

			When validating .NET changes, call `collect_validation_evidence` for `dotnet_build` or `dotnet_test` with a repository-relative solution/project path instead of inventing shell commands.

			If the imported stage prompt asks you to post a started/progress/verdict/verification/landing comment, call `render_stage_comment` and return the rendered body in the required stage artifact instead of using shell or GitHub commands to post from the SDK session.
			""";
	}

	private static string BuildRepositoryProfileContext(string? profileSummary)
	{
		if (string.IsNullOrWhiteSpace(profileSummary))
		{
			return string.Empty;
		}

		return $$"""

			## Target Repository Profile

			Use this detected target-repository context when choosing validation, documentation, and implementation commands:
			{{profileSummary.Trim()}}
			""";
	}

	private static string BuildCommandGuidance(CyberpilotRuntimePreferences? preferences)
	{
		var commandStyle = ResolveCommandStyle(preferences?.CommandStyle ?? CommandStylePreference.Auto);
		return commandStyle switch
		{
			CommandStylePreference.Windows => """

				## Command Style

				This run prefers Windows/PowerShell-native command syntax:
				- Use PowerShell commands and pipelines (`Get-ChildItem`, `Select-String`, `Select-Object -Last 20`) instead of Unix utilities (`ls`, `grep`, `tail`, `head`, `cat`) unless you first verify the utility exists.
				- Use Windows paths with backslashes when constructing file paths.
				- Use `2>&1 | Select-Object -Last <n>` for compact command output instead of `| tail -n <n>`.
				""",
			CommandStylePreference.Linux => """

				## Command Style

				This run prefers Linux/POSIX shell command syntax:
				- Use POSIX shell utilities (`ls`, `grep`, `tail`, `head`, `cat`) and forward-slash paths when constructing commands.
				- Use `2>&1 | tail -n <n>` for compact command output.
				- Avoid PowerShell-specific cmdlets unless you first verify PowerShell is available.
				""",
			_ => string.Empty,
		};
	}

	private static CommandStylePreference ResolveCommandStyle(CommandStylePreference commandStyle)
	{
		if (commandStyle != CommandStylePreference.Auto)
		{
			return commandStyle;
		}

		return OperatingSystem.IsWindows() ? CommandStylePreference.Windows : CommandStylePreference.Linux;
	}

	private static string BuildReportingGuidance(string stageName)
	{
		if (!stageName.Equals("deliver", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		return """

				## Landing Report Evidence

				When you post the final landing report, include a compact evidence and policy summary:
				- Link to the merged pull request or relevant PR evidence.
				- Mention the validation, documentation, and delivery artifacts that support the landing decision.
				- Summarize policy signals, gate outcomes, approvals, and any required actions that were resolved.
				- Keep raw transcripts out of the report; cite concise evidence links or summaries instead.
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
