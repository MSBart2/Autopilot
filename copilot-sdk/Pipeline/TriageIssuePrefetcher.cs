using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cyberpilot.Pipeline;

/// <summary>
/// Fetches GitHub issue data before the triage stage starts so the agent skips redundant issue-read tool calls.
/// Returns a compact markdown block ready for injection into the prompt. Returns <see langword="null"/> on any failure so the agent can proceed without prefetch.
/// </summary>
internal static class TriageIssuePrefetcher
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	/// <summary>
	/// Runs <c>gh issue view &lt;issueNumber&gt; --json title,body,labels,comments</c> in <paramref name="repoRoot"/>
	/// and returns a compact markdown context block. Returns <see langword="null"/> on any failure.
	/// </summary>
	public static async Task<string?> FetchAsync(int issueNumber, string repoRoot, CancellationToken cancellationToken = default)
	{
		try
		{
			var json = await RunGhAsync(issueNumber, repoRoot, cancellationToken);
			if (string.IsNullOrWhiteSpace(json))
			{
				return null;
			}

			return FormatContextBlock(issueNumber, json);
		}
		catch
		{
			return null;
		}
	}

	private static async Task<string?> RunGhAsync(int issueNumber, string repoRoot, CancellationToken cancellationToken)
	{
		var psi = new ProcessStartInfo("gh", $"issue view {issueNumber} --json title,body,labels,comments")
		{
			WorkingDirectory = repoRoot,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		using var process = Process.Start(psi);
		if (process is null)
		{
			return null;
		}

		var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return process.ExitCode == 0 ? output : null;
	}

	private static string? FormatContextBlock(int issueNumber, string json)
	{
		JsonNode? root;
		try
		{
			root = JsonNode.Parse(json);
		}
		catch (JsonException)
		{
			return null;
		}

		if (root is null)
		{
			return null;
		}

		var title = root["title"]?.GetValue<string>() ?? "(untitled)";
		var body = root["body"]?.GetValue<string>() ?? string.Empty;

		var labels = root["labels"]?.AsArray()
			.Select(label => label?["name"]?.GetValue<string>())
			.Where(name => name is not null)
			.ToArray() ?? [];

		var comments = root["comments"]?.AsArray() ?? [];

		var sb = new StringBuilder();
		sb.AppendLine("## Pre-fetched Issue Context");
		sb.AppendLine($"> Issue #{issueNumber} data was fetched before the agent started. Do not re-read this issue with a tool call — work from the context below.");
		sb.AppendLine();
		sb.AppendLine($"**Title:** {title}");

		if (labels.Length > 0)
		{
			sb.AppendLine($"**Labels:** {string.Join(", ", labels)}");
		}

		sb.AppendLine();
		sb.AppendLine("**Issue Body:**");
		sb.AppendLine();

		var truncatedBody = TruncateText(body, 2000);
		sb.AppendLine(truncatedBody);

		var recentComments = comments.Count > 2
			? comments.Skip(comments.Count - 2).ToArray()
			: comments.ToArray();

		if (recentComments.Length > 0)
		{
			sb.AppendLine();
			sb.AppendLine($"**Comments** ({comments.Count} total; showing last {recentComments.Length}):");
			sb.AppendLine();

			foreach (var comment in recentComments)
			{
				var author = comment?["author"]?["login"]?.GetValue<string>() ?? "unknown";
				var commentBody = comment?["body"]?.GetValue<string>() ?? string.Empty;
				sb.AppendLine($"— **{author}:** {TruncateText(commentBody, 500)}");
				sb.AppendLine();
			}
		}

		return sb.ToString().TrimEnd();
	}

	private static string TruncateText(string text, int maxLength)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
		{
			return text;
		}

		return text[..maxLength] + "\n\n_(truncated)_";
	}
}
