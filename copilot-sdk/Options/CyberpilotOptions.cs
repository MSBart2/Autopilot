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
    int? PrNumber = null,
    string? AgentPromptRoot = null,
    IReadOnlyDictionary<string, string>? StageModelOverrides = null,
    IReadOnlyDictionary<string, string>? StageModelFallbacks = null,
    bool ResetMode = false,
    bool BenchmarkReset = false,
    CyberpilotRuntimePreferences? RuntimePreferences = null,
    string? OnlyStage = null,
    int BenchmarkRepeat = 1,
    string? ExperimentVariant = null,
    IReadOnlyDictionary<string, string>? SeedStageResultVariants = null,
    string? RunId = null)
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
            "                       Persist detailed redacted tool output as diagnostic artifacts. Defaults to off.",
            "  --use-harness-system-message",
            "                       Alias for --system-message-mode append. Inject harness law via SDK system message instead of repeating it in the user prompt.",
            "  --system-message-mode <none|append|replace>",
            "                       How to deliver harness law to the SDK session. none=inline (default), append=add after built-in Copilot guidance, replace=replace built-in guidance entirely.",
            "  --system-message-profile <full|lean>",
            "                       System-message harness guidance profile. full=current guidance, lean=compact benchmark guidance.",
            "  --parallel-review-dimensions",
            "                       Run read-only review specialist dimensions concurrently and merge a deterministic verdict.",
            "  --only-stage <name>  Run only this stage then stop (e.g. triage, plan, implement). Implies --start-stage.",
            "  --pr-head-branch <branch>",
            "                       Known pull request head branch for PR-first review runs.",
            "  --pr-number <number>",
            "                       Known pull request number for PR-first review runs.",
            "  --repeat <n>         Run the stage N times, resetting between iterations. Use with --only-stage for benchmarking.",
            "  --variant <name>     Tag these runs in the database with an experiment variant name.",
            "  --seed-stage-result <stage>=<variant>",
            "                       Seed prior stage history from a completed DB run variant before executing.",
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

        // Issue number validation is deferred to CyberpilotApp after config applies BenchmarkIssue.

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
            PrHeadBranch: string.IsNullOrWhiteSpace(parsed.PrHeadBranch) ? null : parsed.PrHeadBranch,
            PrNumber: parsed.PrNumber,
            AgentPromptRoot: string.IsNullOrWhiteSpace(parsed.AgentPromptRoot) ? null : Path.GetFullPath(parsed.AgentPromptRoot),
            StageModelOverrides: parsed.StageModelOverrides,
            StageModelFallbacks: parsed.StageModelFallbacks,
            ResetMode: parsed.ResetMode,
            BenchmarkReset: parsed.BenchmarkReset,
            RuntimePreferences: parsed.RuntimePreferences,
            OnlyStage: parsed.OnlyStage,
            BenchmarkRepeat: parsed.BenchmarkRepeat,
            ExperimentVariant: parsed.ExperimentVariant,
            SeedStageResultVariants: parsed.SeedStageResultVariants,
            StartStage: parsed.OnlyStage ?? parsed.StartStage);
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
        string? PrHeadBranch = null,
        int? PrNumber = null,
        string? AgentPromptRoot = null,
        IReadOnlyDictionary<string, string>? StageModelOverrides = null,
        IReadOnlyDictionary<string, string>? StageModelFallbacks = null,
        bool ResetMode = false,
        bool BenchmarkReset = false,
        CyberpilotRuntimePreferences? RuntimePreferences = null,
        string? OnlyStage = null,
        int BenchmarkRepeat = 1,
        string? ExperimentVariant = null,
        string? StartStage = null,
        IReadOnlyDictionary<string, string>? SeedStageResultVariants = null)
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
                    parsed = parsed with { RuntimePreferences = parsed.RuntimePreferences.WithSystemMessageMode(HarnessSystemMessageMode.Append) };
                    break;
                case "--system-message-mode":
                    parsed = parsed with { RuntimePreferences = parsed.RuntimePreferences.WithSystemMessageMode(ParseSystemMessageMode(RequireNonEmptyValue(args, ref index, arg), arg)) };
                    break;
                case "--system-message-profile":
                    parsed = parsed with { RuntimePreferences = parsed.RuntimePreferences.WithSystemMessageProfile(ParseSystemMessageProfile(RequireNonEmptyValue(args, ref index, arg), arg)) };
                    break;
                case "--parallel-review-dimensions":
                    parsed = parsed with { RuntimePreferences = parsed.RuntimePreferences.WithParallelReviewDimensions(true) };
                    break;
                case "--only-stage":
                    parsed = parsed with { OnlyStage = RequireNonEmptyValue(args, ref index, arg).ToLowerInvariant() };
                    break;
                case "--pr-head-branch":
                    parsed = parsed with { PrHeadBranch = RequireNonEmptyValue(args, ref index, arg) };
                    break;
                case "--pr-number":
                    parsed = parsed with { PrNumber = ParsePositiveInt(RequireValue(args, ref index, arg), arg) };
                    break;
                case "--repeat":
                    parsed = parsed with { BenchmarkRepeat = ParsePositiveInt(RequireValue(args, ref index, arg), arg) };
                    break;
                case "--variant":
                    parsed = parsed with { ExperimentVariant = RequireNonEmptyValue(args, ref index, arg) };
                    break;
                case "--seed-stage-result":
                    parsed = parsed with { SeedStageResultVariants = AddSeedStageResult(parsed.SeedStageResultVariants, RequireNonEmptyValue(args, ref index, arg), arg) };
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

    public HarnessSystemMessageMode SystemMessageMode => Preferences.SystemMessageMode;

    public HarnessSystemMessageProfile SystemMessageProfile => Preferences.SystemMessageProfile;

    public bool ParallelReviewDimensions => Preferences.ParallelReviewDimensions;

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

    private static IReadOnlyDictionary<string, string> AddSeedStageResult(IReadOnlyDictionary<string, string>? current, string value, string optionName)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException($"{optionName} expects <stage>=<variant>.");
        }

        var stageName = value[..separator].Trim().ToLowerInvariant();
        var variant = value[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(stageName) || string.IsNullOrWhiteSpace(variant))
        {
            throw new ArgumentException($"{optionName} expects <stage>=<variant>.");
        }

        var updated = new Dictionary<string, string>(current ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [stageName] = variant,
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

    private static HarnessSystemMessageMode ParseSystemMessageMode(string value, string optionName)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "none" => HarnessSystemMessageMode.None,
            "append" => HarnessSystemMessageMode.Append,
            "replace" => HarnessSystemMessageMode.Replace,
            _ => throw new ArgumentException($"{optionName} expects one of: none, append, replace."),
        };
    }

    private static HarnessSystemMessageProfile ParseSystemMessageProfile(string value, string optionName)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "full" => HarnessSystemMessageProfile.Full,
            "lean" => HarnessSystemMessageProfile.Lean,
            _ => throw new ArgumentException($"{optionName} expects one of: full, lean."),
        };
    }

    private static int ParsePositiveInt(string value, string optionName)
    {
        if (!int.TryParse(value, out var n) || n <= 0)
        {
            throw new ArgumentException($"{optionName} requires a positive integer.");
        }

        return n;
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

    public static CyberpilotRuntimePreferences WithSystemMessageMode(this CyberpilotRuntimePreferences? preferences, HarnessSystemMessageMode mode)
    {
        var current = preferences ?? CyberpilotRuntimePreferences.Default;
        return current with { SystemMessageMode = mode, SystemMessageModeConfigured = true };
    }

    public static CyberpilotRuntimePreferences WithSystemMessageProfile(this CyberpilotRuntimePreferences? preferences, HarnessSystemMessageProfile profile)
    {
        var current = preferences ?? CyberpilotRuntimePreferences.Default;
        return current with { SystemMessageProfile = profile, SystemMessageProfileConfigured = true };
    }

    public static CyberpilotRuntimePreferences WithParallelReviewDimensions(this CyberpilotRuntimePreferences? preferences, bool parallelReviewDimensions)
    {
        var current = preferences ?? CyberpilotRuntimePreferences.Default;
        return current with { ParallelReviewDimensions = parallelReviewDimensions };
    }
}
