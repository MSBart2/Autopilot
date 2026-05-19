using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Cyberpilot.GitHub;
using Cyberpilot.Pipeline;
using Microsoft.Extensions.AI;

namespace Cyberpilot.Copilot;

internal sealed class PipelineContextToolProvider(PipelineExecutionContext context, StageDefinition stage, IGitHubCli gitHubCli)
{
    private const int MaxRenderedCommentSummaryLength = 2800;
    private const int MaxFileContentLength = 1500;
    private const int MaxValidationOutputLength = 2000;

    public ICollection<AIFunction> CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(
                (CancellationToken cancellationToken) => GetPipelineContextAsync(cancellationToken),
                "get_pipeline_context",
                "Returns compact Cyberpilot run context including issue, repository, branch, PR, stage history, and artifact summaries."),
            AIFunctionFactory.Create(
                (CancellationToken cancellationToken) => GetPullRequestDetailsAsync(cancellationToken),
                "get_pr_details",
                "Returns compact pull request metadata for the current Cyberpilot run."),
            AIFunctionFactory.Create(
                (int maxFiles, CancellationToken cancellationToken) => GetPullRequestDiffSummaryAsync(maxFiles, cancellationToken),
                "get_pr_diff_summary",
                "Returns compact pull request diff stats, touched areas, review signals, and a reference to detailed persisted output."),
            AIFunctionFactory.Create(
                (string commentKind, string summary, CancellationToken cancellationToken) => RenderStageCommentAsync(commentKind, summary, cancellationToken),
                "render_stage_comment",
                "Renders a deterministic Markdown stage comment body without posting to GitHub."),
            AIFunctionFactory.Create(
                (string path, int maxChars, CancellationToken cancellationToken) => GetChangedFileContentAsync(path, maxChars, cancellationToken),
                "get_changed_file_content",
                "Reads a repository-relative changed file with line numbers, avoiding absolute path read failures."),
            AIFunctionFactory.Create(
                (string validationKind, string targetPath, int timeoutSeconds, CancellationToken cancellationToken) => CollectValidationEvidenceAsync(validationKind, targetPath, timeoutSeconds, cancellationToken),
                "collect_validation_evidence",
                "Runs deterministic validation commands such as dotnet build/test and returns compact typed evidence."),
        ];
    }

    public Task<PipelineToolResponse<StageContextSnapshot>> GetPipelineContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = context.CreateStageContext(stage.Name);

        return Task.FromResult(PipelineToolResponse<StageContextSnapshot>.Ok(result));
    }

    public async Task<PipelineToolResponse<PullRequestDetailsToolResult>> GetPullRequestDetailsAsync(CancellationToken cancellationToken = default)
    {
        var prNumber = context.PullRequestNumber;
        if (prNumber is null or <= 0)
        {
            return PipelineToolResponse<PullRequestDetailsToolResult>.Fail("missing_pr", "No pull request is known for this run. Create or link a PR before requesting PR details.");
        }

        try
        {
            var raw = await gitHubCli.RunAsync(
                ["pr", "view", prNumber.Value.ToString(), "--json", "number,title,state,url,headRefName,baseRefName,author,mergeable,reviewDecision,changedFiles,additions,deletions,labels,isDraft"],
                allowFailure: false,
                cancellationToken);
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var result = new PullRequestDetailsToolResult(
                ReadInt(root, "number") ?? prNumber.Value,
                ReadString(root, "title"),
                ReadString(root, "state"),
                ReadString(root, "url") ?? context.PrUrl,
                ReadString(root, "headRefName") ?? context.HeadBranch,
                ReadString(root, "baseRefName"),
                ReadAuthorLogin(root),
                ReadString(root, "mergeable"),
                ReadString(root, "reviewDecision"),
                ReadBool(root, "isDraft"),
                ReadInt(root, "changedFiles"),
                ReadInt(root, "additions"),
                ReadInt(root, "deletions"),
                ReadLabels(root));
            context.PrUrl = result.Url ?? context.PrUrl;
            context.BaseBranch = result.BaseRefName ?? context.BaseBranch;
            var reference = PersistToolOutput("get_pr_details", raw, "application/json");
            return PipelineToolResponse<PullRequestDetailsToolResult>.Ok(result, reference);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PipelineToolResponse<PullRequestDetailsToolResult>.Fail("pr_details_failed", $"Unable to load PR details for #{prNumber}: {ex.Message}");
        }
    }

    public async Task<PipelineToolResponse<PullRequestDiffSummaryToolResult>> GetPullRequestDiffSummaryAsync(int maxFiles = 40, CancellationToken cancellationToken = default)
    {
        var prNumber = context.PullRequestNumber;
        if (prNumber is null or <= 0)
        {
            return PipelineToolResponse<PullRequestDiffSummaryToolResult>.Fail("missing_pr", "No pull request is known for this run. Create or link a PR before requesting a diff summary.");
        }

        var limit = Math.Clamp(maxFiles <= 0 ? 40 : maxFiles, 1, 100);
        try
        {
            var raw = await gitHubCli.RunAsync(
                ["pr", "view", prNumber.Value.ToString(), "--json", "number,url,changedFiles,additions,deletions,files"],
                allowFailure: false,
                cancellationToken);
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var files = ReadFiles(root).ToArray();
            var result = new PullRequestDiffSummaryToolResult(
                ReadInt(root, "number") ?? prNumber.Value,
                ReadString(root, "url") ?? context.PrUrl,
                ReadInt(root, "changedFiles") ?? files.Length,
                ReadInt(root, "additions"),
                ReadInt(root, "deletions"),
                files.Take(limit).ToArray(),
                files.Length > limit,
                GroupBy(files, file => file.TopDirectory),
                GroupBy(files, file => file.Extension),
                BuildSignals(files));
            var reference = PersistToolOutput("get_pr_diff_summary", raw, "application/json");
            return PipelineToolResponse<PullRequestDiffSummaryToolResult>.Ok(result, reference);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PipelineToolResponse<PullRequestDiffSummaryToolResult>.Fail("pr_diff_summary_failed", $"Unable to load PR diff summary for #{prNumber}: {ex.Message}");
        }
    }

    public Task<PipelineToolResponse<StageCommentToolResult>> RenderStageCommentAsync(string commentKind, string summary, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(commentKind))
        {
            return Task.FromResult(PipelineToolResponse<StageCommentToolResult>.Fail("missing_comment_kind", "Comment kind is required. Use started, progress, verdict, verification, or landing_report."));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return Task.FromResult(PipelineToolResponse<StageCommentToolResult>.Fail("missing_summary", "A concise comment summary is required."));
        }

        var normalizedKind = NormalizeCommentKind(commentKind);
        if (normalizedKind is null)
        {
            return Task.FromResult(PipelineToolResponse<StageCommentToolResult>.Fail("unsupported_comment_kind", $"Unsupported comment kind '{commentKind}'. Use started, progress, verdict, verification, or landing_report."));
        }

        var target = context.PullRequestNumber is > 0 ? $"PR #{context.PullRequestNumber}" : $"issue #{context.Options.IssueNumber}";
        var compactSummary = Truncate(summary.Trim(), MaxRenderedCommentSummaryLength);
        var heading = BuildStageCommentHeading(stage.Name, normalizedKind, context.Options.IssueNumber);
        var body = BuildStageCommentBody(heading, normalizedKind, compactSummary, target);
        var result = new StageCommentToolResult(
            stage.Name,
            normalizedKind,
            target,
            RequiredArtifactName(stage.Name),
            heading,
            body,
            "Return this body in the stage result artifact; do not post it from read-only stages.");

        return Task.FromResult(PipelineToolResponse<StageCommentToolResult>.Ok(result));
    }

    public async Task<PipelineToolResponse<ChangedFileContentToolResult>> GetChangedFileContentAsync(string path, int maxChars = MaxFileContentLength, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedPath = NormalizeRepoRelativePath(path);
        if (normalizedPath is null)
        {
            return PipelineToolResponse<ChangedFileContentToolResult>.Fail("invalid_path", "Path must be repository-relative and may not contain rooted paths, drive letters, or '..' segments.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(context.RepoRoot, normalizedPath));
        var repoRoot = Path.GetFullPath(context.RepoRoot);
        if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return PipelineToolResponse<ChangedFileContentToolResult>.Fail("path_outside_repo", "Path resolves outside the repository root.");
        }

        if (!File.Exists(fullPath))
        {
            return PipelineToolResponse<ChangedFileContentToolResult>.Fail("file_not_found", $"File '{normalizedPath}' was not found under the repository root.");
        }

        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var limit = Math.Clamp(maxChars <= 0 ? MaxFileContentLength : maxChars, 300, MaxFileContentLength);
        var truncated = content.Length > limit;
        var visibleContent = truncated ? Truncate(content, limit) : content;
        var result = new ChangedFileContentToolResult(
            normalizedPath.Replace('\\', '/'),
            content.Length,
            CountLines(content),
            truncated,
            AddLineNumbers(visibleContent));

        return PipelineToolResponse<ChangedFileContentToolResult>.Ok(result);
    }

    public async Task<PipelineToolResponse<ValidationEvidenceToolResult>> CollectValidationEvidenceAsync(
        string validationKind,
        string targetPath,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedKind = NormalizeValidationKind(validationKind);
        if (normalizedKind is null)
        {
            return PipelineToolResponse<ValidationEvidenceToolResult>.Fail("unsupported_validation", "Validation kind must be dotnet_build or dotnet_test.");
        }

        var normalizedTarget = NormalizeRepoRelativePath(targetPath);
        if (normalizedTarget is null)
        {
            return PipelineToolResponse<ValidationEvidenceToolResult>.Fail("invalid_target_path", "Target path must be repository-relative and may not contain rooted paths, drive letters, or '..' segments.");
        }

        var fullTargetPath = Path.GetFullPath(Path.Combine(context.RepoRoot, normalizedTarget));
        var repoRoot = Path.GetFullPath(context.RepoRoot);
        if (!fullTargetPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return PipelineToolResponse<ValidationEvidenceToolResult>.Fail("target_outside_repo", "Target path resolves outside the repository root.");
        }

        if (!File.Exists(fullTargetPath))
        {
            return PipelineToolResponse<ValidationEvidenceToolResult>.Fail("target_not_found", $"Validation target '{normalizedTarget}' was not found under the repository root.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds <= 0 ? 120 : timeoutSeconds, 10, 600));
        var args = normalizedKind == "dotnet_test"
            ? new[] { "test", normalizedTarget, "--no-restore", "--verbosity", "normal" }
            : ["build", normalizedTarget, "--no-restore", "--verbosity", "minimal"];

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = context.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet process.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        string output;
        string error;
        try
        {
            output = await process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            error = await process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process exited between timeout cancellation and cleanup.
            }

            stopwatch.Stop();
            var timedOut = new ValidationEvidenceToolResult(
                normalizedKind,
                $"dotnet {string.Join(' ', args)}",
                normalizedTarget.Replace('\\', '/'),
                false,
                null,
                true,
                stopwatch.ElapsedMilliseconds,
                $"Validation timed out after {(int)timeout.TotalSeconds} seconds.",
                null);
            return PipelineToolResponse<ValidationEvidenceToolResult>.Ok(timedOut);
        }

        stopwatch.Stop();
        var combined = string.IsNullOrWhiteSpace(error) ? output : $"{output}{Environment.NewLine}{error}";
        var result = new ValidationEvidenceToolResult(
            normalizedKind,
            $"dotnet {string.Join(' ', args)}",
            normalizedTarget.Replace('\\', '/'),
            process.ExitCode == 0,
            process.ExitCode,
            false,
            stopwatch.ElapsedMilliseconds,
            Tail(Truncate(combined.Trim(), MaxValidationOutputLength), 80),
            process.ExitCode == 0 ? null : "Validation command exited non-zero.");

        return PipelineToolResponse<ValidationEvidenceToolResult>.Ok(result);
    }

    private ToolOutputReference? PersistToolOutput(string toolName, string rawOutput, string mediaType)
    {
        if (!context.Options.CaptureToolOutputArtifacts)
        {
            return null;
        }

        var artifactName = $"tool-output-{toolName}";
        var uri = $"cyberpilot://tool-output/{Uri.EscapeDataString(stage.Name)}/{Uri.EscapeDataString(toolName)}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        context.RecordToolArtifact(stage.Name, new StageArtifact(artifactName, Truncate(rawOutput, 3800), uri, mediaType));
        return new ToolOutputReference(artifactName, uri);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength), "...[truncated]");
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetString() : null;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : null;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;
    }

    private static string? ReadAuthorLogin(JsonElement element)
    {
        return element.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object ? ReadString(author, "login") : null;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement element)
    {
        if (!element.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return labels.EnumerateArray()
            .Select(label => ReadString(label, "name"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static IEnumerable<PullRequestFileSummary> ReadFiles(JsonElement element)
    {
        if (!element.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var file in files.EnumerateArray())
        {
            var path = ReadString(file, "path") ?? ReadString(file, "filename") ?? ReadString(file, "name");
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            yield return new PullRequestFileSummary(
                path,
                ReadInt(file, "additions"),
                ReadInt(file, "deletions"),
                ReadString(file, "status") ?? ReadString(file, "changeType"));
        }
    }

    private static IReadOnlyList<PullRequestDiffGroup> GroupBy(
        IReadOnlyList<PullRequestFileSummary> files,
        Func<PullRequestFileSummary, string> keySelector)
    {
        return files
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PullRequestDiffGroup(
                group.Key,
                group.Count(),
                SumNullable(group.Select(file => file.Additions)),
                SumNullable(group.Select(file => file.Deletions))))
            .OrderByDescending(group => group.FileCount)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int? SumNullable(IEnumerable<int?> values)
    {
        var total = 0;
        var hasValue = false;
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            total += value.Value;
            hasValue = true;
        }

        return hasValue ? total : null;
    }

    private static IReadOnlyList<string> BuildSignals(IReadOnlyList<PullRequestFileSummary> files)
    {
        var paths = files.Select(file => file.Path).ToArray();
        var signals = new List<string>();

        AddSignal(signals, paths.Any(IsProductionCode), "production_code_changed");
        AddSignal(signals, paths.Any(IsTestCode), "test_code_changed");
        AddSignal(signals, paths.Any(IsDocumentation), "documentation_changed");
        AddSignal(signals, paths.Any(IsWebSurface), "web_surface_changed");
        AddSignal(signals, paths.Any(IsConfiguration), "configuration_changed");

        return signals;
    }

    private static string? NormalizeCommentKind(string value)
    {
        return value.Trim().ToLowerInvariant().Replace("-", "_") switch
        {
            "start" or "started" or "review_started" => "started",
            "progress" or "update" => "progress",
            "verdict" or "decision" or "review_verdict" => "verdict",
            "verification" or "docs_verification" => "verification",
            "landing" or "landing_report" or "deliver" => "landing_report",
            _ => null,
        };
    }

    private static string BuildStageCommentHeading(string stageName, string commentKind, int issueNumber)
    {
        if (stageName.Equals("review", StringComparison.OrdinalIgnoreCase) && commentKind == "verdict")
        {
            return "## 🎸 The Critic's Verdict";
        }

        if (stageName.Equals("docs", StringComparison.OrdinalIgnoreCase) && commentKind == "verification")
        {
            return $"## 📚 Docs & Verification — Issue #{issueNumber}";
        }

        if (stageName.Equals("deliver", StringComparison.OrdinalIgnoreCase) || commentKind == "landing_report")
        {
            return $"## 🚀 Landing Report — Issue #{issueNumber}";
        }

        var stageLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stageName.Replace("_", " ").Replace("-", " "));
        var kindLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(commentKind.Replace("_", " "));
        var emoji = stageName.ToLowerInvariant() switch
        {
            "triage" => "🧭",
            "plan" => "📝",
            "implement" => "🛠️",
            "review" => "🎸",
            "docs" => "📚",
            _ => "🤖",
        };

        return $"## {emoji} {stageLabel} {kindLabel} — Issue #{issueNumber}";
    }

    private static string BuildStageCommentBody(string heading, string commentKind, string summary, string target)
    {
        var kindLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(commentKind.Replace("_", " "));
        return string.Join(Environment.NewLine, [
            heading,
            "",
            $"**Target:** {target}",
            $"**Update type:** {kindLabel}",
            "",
            summary,
        ]);
    }

    private static string RequiredArtifactName(string stageName)
    {
        return stageName.ToLowerInvariant() switch
        {
            "triage" => "triage-comment",
            "plan" => "plan-comment",
            "implement" => "pull-request",
            "review" => "review-verdict",
            "docs" => "documentation-summary",
            "deliver" => "landing-report",
            _ => "stage-comment",
        };
    }

    private static string? NormalizeRepoRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(trimmed) || trimmed.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        var segments = trimmed.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
        {
            return null;
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static string AddLineNumbers(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join(Environment.NewLine, lines.Select((line, index) => $"{index + 1}. {line}"));
    }

    private static int CountLines(string content)
    {
        return content.Length == 0 ? 0 : content.Count(character => character == '\n') + 1;
    }

    private static string Tail(string value, int maxLines)
    {
        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
    }

    private static string? NormalizeValidationKind(string? validationKind)
    {
        return validationKind?.Trim().ToLowerInvariant().Replace("-", "_") switch
        {
            "build" or "dotnet_build" => "dotnet_build",
            "test" or "dotnet_test" => "dotnet_test",
            _ => null,
        };
    }

    private static void AddSignal(List<string> signals, bool condition, string signal)
    {
        if (condition)
        {
            signals.Add(signal);
        }
    }

    private static bool IsProductionCode(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
           && !IsTestCode(path)
           && !path.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestCode(string path)
        => path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)
           || path.Contains(".Tests/", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsDocumentation(string path)
        => path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static bool IsWebSurface(string path)
        => path.StartsWith("web/Controllers/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("web/Models/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("web/Views/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("web/wwwroot/", StringComparison.OrdinalIgnoreCase);

    private static bool IsConfiguration(string path)
        => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
           || path.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
           || path.Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase);
}

internal sealed record PipelineToolResponse<T>(bool Success, T? Data, PipelineToolError? Error, ToolOutputReference? DetailedOutput)
{
    public static PipelineToolResponse<T> Ok(T data, ToolOutputReference? detailedOutput = null) => new(true, data, null, detailedOutput);

    public static PipelineToolResponse<T> Fail(string code, string message) => new(false, default, new PipelineToolError(code, message), null);
}

internal sealed record PipelineToolError(string Code, string Message);

internal sealed record ToolOutputReference(string ArtifactName, string Uri);

internal sealed record PullRequestDetailsToolResult(
    int Number,
    string? Title,
    string? State,
    string? Url,
    string? HeadRefName,
    string? BaseRefName,
    string? AuthorLogin,
    string? Mergeable,
    string? ReviewDecision,
    bool? IsDraft,
    int? ChangedFiles,
    int? Additions,
    int? Deletions,
    IReadOnlyList<string> Labels);

internal sealed record PullRequestDiffSummaryToolResult(
    int Number,
    string? Url,
    int ChangedFiles,
    int? Additions,
    int? Deletions,
    IReadOnlyList<PullRequestFileSummary> Files,
    bool Truncated,
    IReadOnlyList<PullRequestDiffGroup> TopDirectories,
    IReadOnlyList<PullRequestDiffGroup> Extensions,
    IReadOnlyList<string> Signals);

internal sealed record PullRequestDiffGroup(string Name, int FileCount, int? Additions, int? Deletions);

internal sealed record StageCommentToolResult(
    string StageName,
    string CommentKind,
    string Target,
    string SuggestedArtifactName,
    string Heading,
    string Body,
    string Usage);

internal sealed record ChangedFileContentToolResult(
    string Path,
    int CharacterCount,
    int LineCount,
    bool Truncated,
    string NumberedContent);

internal sealed record ValidationEvidenceToolResult(
    string ValidationKind,
    string Command,
    string TargetPath,
    bool Passed,
    int? ExitCode,
    bool TimedOut,
    long DurationMs,
    string OutputTail,
    string? Error);

internal sealed record PullRequestFileSummary(string Path, int? Additions, int? Deletions, string? Status)
{
    public string TopDirectory
    {
        get
        {
            var separator = Path.IndexOf('/');
            return separator > 0 ? Path[..separator] : "(root)";
        }
    }

    public string Extension
    {
        get
        {
            var extension = System.IO.Path.GetExtension(Path);
            return string.IsNullOrWhiteSpace(extension) ? "(none)" : extension.ToLowerInvariant();
        }
    }
}
