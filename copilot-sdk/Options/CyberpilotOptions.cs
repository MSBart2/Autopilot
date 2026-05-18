using Cyberpilot.Pipeline;

namespace Cyberpilot.Options;

internal sealed record CyberpilotOptions(
    int IssueNumber,
    string RepoRoot,
    string? Repository,
    string Model,
    bool SkipDeliver,
    bool EnsureLabels,
    bool CheckLabelsOnly,
    bool CheckModelOnly,
    TimeSpan StageTimeout,
    bool ApproveAll,
    bool AllowMissingDocs,
    string? DatabaseConnectionString,
    string? ConfigPath,
    bool ShowHelp,
    string? StartStage = null,
    Func<CancellationToken, Task<bool>>? ShouldPauseAsync = null,
    string PipelineDefinitionName = PipelineDefinitionDefaults.DefinitionName,
    string PipelineDefinitionVersion = PipelineDefinitionDefaults.DefinitionVersion,
    string PolicyProfileName = PipelineDefinitionDefaults.PolicyProfileName,
    Func<PipelinePauseContext, CancellationToken, Task<PipelinePauseDecision>>? ShouldPauseDecisionAsync = null,
    string? PipelineDefinitionFilePath = null,
    string? PrHeadBranch = null,
    string? AgentPromptRoot = null,
    IReadOnlyDictionary<string, string>? StageModelOverrides = null,
    IReadOnlyDictionary<string, string>? StageModelFallbacks = null,
    bool ResetMode = false,
    bool BenchmarkReset = false,
    CyberpilotRuntimePreferences? RuntimePreferences = null)
{
    public const string DefaultModel = "claude-sonnet-4.6";
    public static readonly TimeSpan DefaultStageTimeout = TimeSpan.FromMinutes(10);

    public static string HelpText => string.Join(Environment.NewLine,
            "Cyberpilot SDK Runner",
            string.Empty,
            "Usage:",
            "  dotnet run --project copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj -- run issue <number> [--repo owner/name] [--model model-id] [--skip-deliver]",
            "  dotnet run --project copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj -- issue <number> [--repo owner/name]",
            "  dotnet run --project copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj -- <number>",
            "  dotnet run --project copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj -- reset issue <number> [--benchmark-reset]",
            "  dotnet run --project copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj -- --check-labels [--repo owner/name]",
            "  dotnet run --project copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj -- --check-model [--model model-id]",
            string.Empty,
            "Options:",
            "  --repo-root <path>   Repository root. Defaults to the nearest parent containing .github/agents.",
            "  --repo <owner/name>  GitHub repository for gh issue operations. Defaults to gh repo view.",
            "  --model <model-id>   Copilot model. Defaults to claude-sonnet-4.6.",
            "  --stage-timeout-minutes <minutes>",
            "                       Wait time for each Copilot stage. Defaults to 10.",
            "  --pipeline-definition <name>",
            $"                       Pipeline definition to run. Available: {BuiltInPipelineCatalog.AvailableDefinitionNames}.",
            "  --pipeline-definition-file <path>",
            "                       Load additional JSON pipeline definitions from a file.",
            "  --pipeline-version <version>",
            "                       Pipeline definition version. Defaults to 1.0.",
            "  --policy-profile <name>",
            $"                       Policy profile to apply: {BuiltInPipelineCatalog.AvailablePolicyProfileNames}.",
            "  --ensure-labels     Create missing sdk labels before running.",
            "  --check-labels      Check sdk labels and exit without running stages.",
            "  --check-model       Check Copilot model availability and exit without running stages.",
            "  --approve-all       Allow Copilot SDK to approve all tool permission requests.",
            "  --allow-missing-docs Continue to deliver if the docs stage fails.",
            "  --agent-prompt-root <path>",
            "                       Root directory containing .github/agents. Defaults to --repo-root.",
            "  --command-style <auto|windows|linux>",
            "                       Preferred command syntax guidance for agents. Defaults to auto.",
            "  --capture-tool-output-artifacts",
            "                       Persist shaped tool output as diagnostic artifacts. Defaults to off.",
            "  --use-harness-system-message",
            "                       Inject harness law via SDK system message (append) instead of repeating it in the user prompt. Defaults to off.",
            "  --db <connection>  Persist this run to the shared Cyberpilot database.",
            "  --config <path>    Load repo/token pairs from an appsettings-style JSON file.",
            "  --skip-deliver      Run through docs but stop before merge/deliver.",
            "  --benchmark-reset   When used with 'reset', preserve run metrics in the database.");

    public static CyberpilotOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            return new CyberpilotOptions(0, string.Empty, null, DefaultModel, false, false, false, false, DefaultStageTimeout, false, false, null, null, true);
        }

        var parsed = ParseArguments(args);

        if ((parsed.IssueNumber is null or <= 0) && !parsed.CheckLabelsOnly && !parsed.CheckModelOnly && !parsed.ResetMode)
        {
            throw new ArgumentException("Provide a positive issue number. Try: dotnet run --project copilot-sdk-exe/Cyberpilot.Sdk.Exe.csproj -- run issue 135");
        }

        return new CyberpilotOptions(
            parsed.IssueNumber ?? 0,
            RepoRootFinder.Find(parsed.RepoRoot),
            parsed.Repository,
            parsed.Model,
            parsed.SkipDeliver,
            parsed.EnsureLabels,
            parsed.CheckLabelsOnly,
            parsed.CheckModelOnly,
            parsed.StageTimeout,
            parsed.ApproveAll,
            parsed.AllowMissingDocs,
            parsed.DatabaseConnectionString,
            parsed.ConfigPath,
            false,
            PipelineDefinitionName: parsed.PipelineDefinitionName,
            PipelineDefinitionVersion: parsed.PipelineDefinitionVersion,
            PolicyProfileName: parsed.PolicyProfileName,
            PipelineDefinitionFilePath: parsed.PipelineDefinitionFilePath,
            AgentPromptRoot: string.IsNullOrWhiteSpace(parsed.AgentPromptRoot) ? null : Path.GetFullPath(parsed.AgentPromptRoot),
            StageModelOverrides: parsed.StageModelOverrides,
            StageModelFallbacks: parsed.StageModelFallbacks,
            ResetMode: parsed.ResetMode,
            BenchmarkReset: parsed.BenchmarkReset,
            RuntimePreferences: parsed.RuntimePreferences);
    }

    private sealed record ParsedArgs(
        int? IssueNumber = null,
        string? RepoRoot = null,
        string? Repository = null,
        string Model = DefaultModel,
        bool SkipDeliver = false,
        bool EnsureLabels = false,
        bool CheckLabelsOnly = false,
        bool CheckModelOnly = false,
        TimeSpan StageTimeout = default,
        bool ApproveAll = false,
        bool AllowMissingDocs = false,
        string? DatabaseConnectionString = null,
        string? ConfigPath = null,
        string PipelineDefinitionName = PipelineDefinitionDefaults.DefinitionName,
        string PipelineDefinitionVersion = PipelineDefinitionDefaults.DefinitionVersion,
        string PolicyProfileName = PipelineDefinitionDefaults.PolicyProfileName,
        string? PipelineDefinitionFilePath = null,
        string? AgentPromptRoot = null,
        IReadOnlyDictionary<string, string>? StageModelOverrides = null,
        IReadOnlyDictionary<string, string>? StageModelFallbacks = null,
        bool ResetMode = false,
        bool BenchmarkReset = false,
        CyberpilotRuntimePreferences? RuntimePreferences = null)
    {
        public static ParsedArgs Default => new() { StageTimeout = DefaultStageTimeout };
    }

    private static ParsedArgs ParseArguments(string[] args)
    {
        var parsed = ParsedArgs.Default;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "run":
                case "issue":
                case "cyberpilot":
                    continue;
                case "reset":
                    parsed = parsed with { ResetMode = true };
                    continue;
                case "--repo-root":
                    parsed = parsed with { RepoRoot = RequireValue(args, ref index, arg) };
                    break;
                case "--repo":
                    parsed = parsed with { Repository = RequireValue(args, ref index, arg) };
                    break;
                case "--model":
                    parsed = parsed with { Model = RequireValue(args, ref index, arg) };
                    break;
                case "--stage-timeout-minutes":
                    parsed = parsed with { StageTimeout = ParsePositiveMinutes(RequireValue(args, ref index, arg), arg) };
                    break;
                case "--pipeline-definition":
                    parsed = parsed with { PipelineDefinitionName = RequireNonEmptyValue(args, ref index, arg) };
                    break;
                case "--pipeline-definition-file":
                    parsed = parsed with { PipelineDefinitionFilePath = RequireNonEmptyValue(args, ref index, arg) };
                    break;
                case "--pipeline-version":
                    parsed = parsed with { PipelineDefinitionVersion = RequireNonEmptyValue(args, ref index, arg) };
                    break;
                case "--policy-profile":
                    parsed = parsed with { PolicyProfileName = RequireNonEmptyValue(args, ref index, arg) };
                    break;
                case "--stage-model":
                    parsed = parsed with { StageModelOverrides = AddStageModel(parsed.StageModelOverrides, RequireNonEmptyValue(args, ref index, arg), arg) };
                    break;
                case "--stage-fallback-model":
                    parsed = parsed with { StageModelFallbacks = AddStageModel(parsed.StageModelFallbacks, RequireNonEmptyValue(args, ref index, arg), arg) };
                    break;
                case "--command-style":
                    parsed = parsed with { RuntimePreferences = parsed.RuntimePreferences.WithCommandStyle(ParseCommandStyle(RequireNonEmptyValue(args, ref index, arg), arg)) };
                    break;
                case "--capture-tool-output-artifacts":
                    parsed = parsed with { RuntimePreferences = parsed.RuntimePreferences.WithCaptureToolOutputArtifacts(true) };
                    break;
                case "--use-harness-system-message":
                    parsed = parsed with { RuntimePreferences = parsed.RuntimePreferences.WithUseHarnessSystemMessage(true) };
                    break;
                case "--skip-deliver":
                    parsed = parsed with { SkipDeliver = true };
                    break;
                case "--ensure-labels":
                    parsed = parsed with { EnsureLabels = true };
                    break;
                case "--check-labels":
                    parsed = parsed with { CheckLabelsOnly = true };
                    break;
                case "--check-model":
                    parsed = parsed with { CheckModelOnly = true };
                    break;
                case "--benchmark-reset":
                    parsed = parsed with { BenchmarkReset = true };
                    break;
                case "--approve-all":
                    parsed = parsed with { ApproveAll = true };
                    break;
                case "--allow-missing-docs":
                    parsed = parsed with { AllowMissingDocs = true };
                    break;
                case "--agent-prompt-root":
                    parsed = parsed with { AgentPromptRoot = RequireValue(args, ref index, arg) };
                    break;
                case "--db":
                    parsed = parsed with { DatabaseConnectionString = RequireValue(args, ref index, arg) };
                    break;
                case "--config":
                    parsed = parsed with { ConfigPath = RequireValue(args, ref index, arg) };
                    break;
                default:
                    if (int.TryParse(arg, out var issueNumber))
                    {
                        parsed = parsed with { IssueNumber = issueNumber };
                    }
                    break;
            }
        }
        return parsed;
    }

    private CyberpilotRuntimePreferences Preferences => RuntimePreferences ?? CyberpilotRuntimePreferences.Default;

    public CommandStylePreference CommandStyle => Preferences.CommandStyle;

    public bool CaptureToolOutputArtifacts => Preferences.CaptureToolOutputArtifacts;

    public bool UseHarnessSystemMessage => Preferences.UseHarnessSystemMessage;

    private static TimeSpan ParsePositiveMinutes(string value, string optionName)
    {
        if (!double.TryParse(value, out var minutes) || minutes <= 0)
        {
            throw new ArgumentException($"{optionName} requires a positive number of minutes.");
        }

        return TimeSpan.FromMinutes(minutes);
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }

    private static string RequireNonEmptyValue(string[] args, ref int index, string optionName)
    {
        var value = RequireValue(args, ref index, optionName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{optionName} requires a non-empty value.");
        }

        return value;
    }

    private static IReadOnlyDictionary<string, string> AddStageModel(IReadOnlyDictionary<string, string>? current, string value, string optionName)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException($"{optionName} expects <stage>=<model>.");
        }

        var stageName = value[..separator].Trim();
        var model = value[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(stageName) || string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException($"{optionName} expects <stage>=<model>.");
        }

        var updated = new Dictionary<string, string>(current ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [stageName] = model,
        };
        return updated;
    }

    private static CommandStylePreference ParseCommandStyle(string value, string optionName)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => CommandStylePreference.Auto,
            "windows" or "powershell" or "pwsh" => CommandStylePreference.Windows,
            "linux" or "posix" or "bash" or "shell" => CommandStylePreference.Linux,
            _ => throw new ArgumentException($"{optionName} expects one of: auto, windows, linux."),
        };
    }
}

internal static class CyberpilotRuntimePreferenceExtensions
{
    public static CyberpilotRuntimePreferences WithCommandStyle(this CyberpilotRuntimePreferences? preferences, CommandStylePreference commandStyle)
    {
        var current = preferences ?? CyberpilotRuntimePreferences.Default;
        return current with { CommandStyle = commandStyle };
    }

    public static CyberpilotRuntimePreferences WithCaptureToolOutputArtifacts(this CyberpilotRuntimePreferences? preferences, bool captureToolOutputArtifacts)
    {
        var current = preferences ?? CyberpilotRuntimePreferences.Default;
        return current with { CaptureToolOutputArtifacts = captureToolOutputArtifacts };
    }

    public static CyberpilotRuntimePreferences WithUseHarnessSystemMessage(this CyberpilotRuntimePreferences? preferences, bool useHarnessSystemMessage)
    {
        var current = preferences ?? CyberpilotRuntimePreferences.Default;
        return current with { UseHarnessSystemMessage = useHarnessSystemMessage };
    }
}
