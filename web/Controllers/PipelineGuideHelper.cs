using Cyberpilot.Web.Models;
using Markdig;

namespace Cyberpilot.Web.Controllers;

internal static class PipelineGuideHelper
{
    private static readonly IReadOnlyDictionary<string, GuideDefinition> GuideFiles = new Dictionary<string, GuideDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["local"] = new("AI-SDLC.md", "Local", "Controller Session", "VS Code Copilot Chat or Copilot CLI orchestration with repository agents.", "Local Mode"),
        ["cloud"] = new("AI-SDLC.md", "Cloud", "Actions Orbit", "GitHub Agentic Workflow automation with review and finish gates.", "Cloud Mode"),
        ["sdk"] = new("AI-SDLC.md", "SDK", "Web Dispatch", "Programmatic Copilot SDK execution for repeatable issue-to-PR workflows.", "SDK Mode")
    };

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static bool TryRenderGuide(string mode, string contentRootPath, out PipelineGuideViewModel viewModel)
    {
        viewModel = null!;
        if (!GuideFiles.TryGetValue(mode, out var guide))
        {
            return false;
        }

        var repositoryRoot = Path.GetFullPath(Path.Combine(contentRootPath, ".."));
        var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, guide.FileName));
        if (!fullPath.StartsWith(repositoryRoot, StringComparison.Ordinal) || !File.Exists(fullPath))
        {
            return false;
        }

        var markdown = File.ReadAllText(fullPath);
        var modeMarkdown = ExtractModeContent(markdown, guide.SectionHeading);
        var html = Markdown.ToHtml(modeMarkdown, MarkdownPipeline);
        viewModel = new PipelineGuideViewModel(guide.Mode, guide.Title, guide.Summary, html, guide.FileName);
        return true;
    }

    private static string ExtractModeContent(string markdown, string sectionHeading)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        var introLines = new List<string>();
        var firstSectionIndex = Array.FindIndex(lines, line => line.StartsWith("## ", StringComparison.Ordinal));
        var introEnd = firstSectionIndex >= 0 ? firstSectionIndex : lines.Length;
        for (var index = 0; index < introEnd; index++)
        {
            introLines.Add(lines[index]);
        }

        var modeLines = ExtractSection(lines, sectionHeading);
        if (modeLines.Count == 0)
        {
            modeLines = lines.ToList();
        }

        var combined = string.Join('\n', introLines).TrimEnd();
        if (modeLines.Count == 0)
        {
            return combined;
        }

        return string.Concat(combined, "\n\n---\n\n", string.Join('\n', modeLines).Trim());
    }

    private static List<string> ExtractSection(string[] lines, string sectionHeading)
    {
        var sectionTitle = $"## {sectionHeading}";
        var sectionLines = new List<string>();
        var inSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (!inSection)
            {
                if (line.Equals(sectionTitle, StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    sectionLines.Add(rawLine);
                }

                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            sectionLines.Add(rawLine);
        }

        return sectionLines;
    }
}

internal sealed record GuideDefinition(string FileName, string Mode, string Title, string Summary, string SectionHeading);
