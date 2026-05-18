namespace Cyberpilot.Pipeline;

internal interface IPromptBuilder
{
	Task<string> BuildAsync(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, PipelineExecutionContext? context = null, CancellationToken cancellationToken = default);
}

internal sealed class PromptBuilder(
	string repoRoot,
	string agentPromptRoot,
	int issueNumber,
	string? targetRepositoryProfileSummary = null,
	CyberpilotRuntimePreferences? runtimePreferences = null) : IPromptBuilder
{
	public async Task<string> BuildAsync(PipelineStageDefinition stageDefinition, string mission, PolicyProfile policyProfile, PipelineExecutionContext? context = null, CancellationToken cancellationToken = default)
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
		var commandGuidance = BuildCommandGuidance(runtimePreferences);

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
			  "issue_number": {{issueNumber}}
			}
			```

			Use these status values when applicable: GO, STOP, DUPLICATE.
			Use these review decision values when applicable: approved, changes_requested, comment.
			When status is STOP or the result needs human correction, populate `required_actions` with concrete next steps.
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
			$"- Issue: #{context.IssueNumber}",
			$"- Repository: {ValueOrPending(context.Repository)}",
			$"- Repository root: {context.RepoRoot}",
			$"- Pipeline definition: {context.Definition.Name} v{context.Definition.Version.Value}",
			$"- Current stage: {stageName}",
		};

		if (ShouldIncludeBranch(stageName))
		{
			lines.Add($"- Head branch: {ValueOrPending(context.HeadBranch)}");
		}

		if (ShouldIncludePullRequest(stageName))
		{
			lines.Add($"- Pull request: {FormatPullRequest(context)}");
		}

		var priorStages = FilterPriorStages(stageName, context.StageHistory);
		if (priorStages.Count > 0)
		{
			lines.Add("- Prior stage summaries:");
			foreach (var priorStage in priorStages)
			{
				lines.Add($"  - {priorStage.StageName}: {priorStage.Status} / {priorStage.Decision}{FormatError(priorStage.Error)}");
				foreach (var artifact in priorStage.Artifacts.Take(3))
				{
					lines.Add($"    - artifact: {artifact}");
				}
				foreach (var evidence in priorStage.Evidence.Take(2))
				{
					lines.Add($"    - evidence: {evidence}");
				}
			}
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static bool ShouldIncludeBranch(string stageName)
	{
		return !stageName.Equals("triage", StringComparison.OrdinalIgnoreCase);
	}

	private static bool ShouldIncludePullRequest(string stageName)
	{
		return stageName.Equals("review", StringComparison.OrdinalIgnoreCase)
			|| stageName.Equals("docs", StringComparison.OrdinalIgnoreCase)
			|| stageName.Equals("deliver", StringComparison.OrdinalIgnoreCase);
	}

	private static IReadOnlyList<StageExecutionSummary> FilterPriorStages(string stageName, IReadOnlyList<StageExecutionSummary> summaries)
	{
		var includedStages = stageName.ToLowerInvariant() switch
		{
			"plan" => new[] { "triage" },
			"implement" => ["triage", "plan"],
			"review" => ["plan", "implement"],
			"docs" => ["implement", "review"],
			"deliver" => ["review", "docs"],
			_ => [],
		};

		return summaries
			.Where(summary => includedStages.Contains(summary.StageName, StringComparer.OrdinalIgnoreCase))
			.ToArray();
	}

	private static string FormatPullRequest(PipelineExecutionContext context)
	{
		if (!string.IsNullOrWhiteSpace(context.PrUrl) && context.PrNumber.HasValue)
		{
			return $"#{context.PrNumber.Value} at {context.PrUrl}";
		}

		if (!string.IsNullOrWhiteSpace(context.PrUrl))
		{
			return context.PrUrl;
		}

		return "not known yet";
	}

	private static string FormatError(string? error)
	{
		return string.IsNullOrWhiteSpace(error) ? string.Empty : $" ({error})";
	}

	private static string ValueOrPending(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? "not known yet" : value;
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
