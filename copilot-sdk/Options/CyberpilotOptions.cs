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
    string? PipelineDefinitionFilePath = null)
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
            "  --db <connection>  Persist this run to the shared Cyberpilot database.",
            "  --config <path>    Load repo/token pairs from an appsettings-style JSON file.",
            "  --skip-deliver      Run through docs but stop before merge/deliver.");

    public static CyberpilotOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            return new CyberpilotOptions(0, string.Empty, null, DefaultModel, false, false, false, false, DefaultStageTimeout, false, false, null, null, true);
        }

        var parsed = ParseArguments(args);

        if ((parsed.IssueNumber is null or <= 0) && !parsed.CheckLabelsOnly && !parsed.CheckModelOnly)
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
            PipelineDefinitionFilePath: parsed.PipelineDefinitionFilePath);
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
        string? PipelineDefinitionFilePath = null)
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
                case "--approve-all":
                    parsed = parsed with { ApproveAll = true };
                    break;
                case "--allow-missing-docs":
                    parsed = parsed with { AllowMissingDocs = true };
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
}
