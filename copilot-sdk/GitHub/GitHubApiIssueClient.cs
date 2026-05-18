using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Cyberpilot.GitHub;

/// <summary>
/// Uses GitHub REST API calls for issue, label, and comment operations.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2234:Pass System.Uri objects instead of strings", Justification = "Relative URI strings are intentional with a fixed BaseAddress on the HttpClient.")]
public sealed class GitHubApiIssueClient : IGitHubIssueClient
{
    private readonly HttpClient httpClient;
    private readonly string repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubApiIssueClient" /> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for GitHub API calls.</param>
    /// <param name="repository">The repository in owner/name form.</param>
    /// <param name="token">The GitHub token.</param>
    public GitHubApiIssueClient(HttpClient httpClient, string repository, string token)
    {
        this.httpClient = httpClient;
        this.repository = repository;
        this.httpClient.BaseAddress ??= new Uri("https://api.github.com/");
        this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Demo1-Cyberpilot");
        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        this.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        this.httpClient.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
        this.httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <inheritdoc />
    public async Task AddIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { labels = new[] { label } });
        await SendAsync(HttpMethod.Post, $"repos/{repository}/issues/{issueNumber}/labels", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { body });
        await SendAsync(HttpMethod.Post, $"repos/{repository}/issues/{issueNumber}/comments", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"repos/{repository}/issues/{issueNumber}/comments?per_page=100", cancellationToken);
        await EnsureSuccessAsync(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return GitHubIssueCommentJson.ParseMany(document.RootElement);
    }

    /// <inheritdoc />
    public async Task DeleteIssueCommentAsync(long commentId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"repos/{repository}/issues/comments/{commentId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task<GitHubIssueSummary?> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"repos/{repository}/issues/{issueNumber}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return GitHubIssueSummaryJson.ParseIssue(document.RootElement);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetIssueLabelsAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        var issue = await GetIssueAsync(issueNumber, cancellationToken);
        return issue?.Labels ?? [];
    }

    /// <inheritdoc />
    public async Task<string> GetIssueStateAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        var issue = await GetIssueAsync(issueNumber, cancellationToken) ?? throw new InvalidOperationException($"Issue #{issueNumber} was not found.");
        return issue.State;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetRepositoryLabelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"repos/{repository}/labels?per_page=100", cancellationToken);
        await EnsureSuccessAsync(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(label => label.GetProperty("name").GetString() ?? string.Empty)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubIssueSummary>> ListOpenIssuesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"repos/{repository}/issues?state=open&per_page=50", cancellationToken);
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        // The GitHub REST API returns PRs alongside issues. Items with a "pull_request" property are PRs.
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Where(item => !item.TryGetProperty("pull_request", out _))
            .Select(GitHubIssueSummaryJson.ParseIssue)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubIssueSummary>> ListOpenPullRequestsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"repos/{repository}/pulls?state=open&per_page=50", cancellationToken);
        if (!response.IsSuccessStatusCode) return [];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(GitHubIssueSummaryJson.ParsePullRequest)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task RemoveIssueLabelAsync(int issueNumber, string label, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"repos/{repository}/issues/{issueNumber}/labels/{Uri.EscapeDataString(label)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task CloseIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { state = "closed" });
        await SendAsync(HttpMethod.Patch, $"repos/{repository}/issues/{issueNumber}", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClosePullRequestAsync(int pullRequestNumber, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { state = "closed" });
        await SendAsync(HttpMethod.Patch, $"repos/{repository}/pulls/{pullRequestNumber}", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GitHubPullRequestInfo?> FindPullRequestForIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        // GitHub REST: list PRs, filter by head branch pattern for the issue
        using var response = await httpClient.GetAsync($"repos/{repository}/pulls?state=open&per_page=30", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var pr in document.RootElement.EnumerateArray())
        {
            var headRef = pr.TryGetProperty("head", out var head) && head.TryGetProperty("ref", out var refProp)
                ? refProp.GetString() ?? "" : "";
            if (GitHubPullRequestMatcher.IsIssueBranch(headRef, issueNumber))
            {
                var number = pr.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
                var url = pr.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
                var state = pr.TryGetProperty("state", out var s) ? s.GetString() ?? "open" : "open";
                return new GitHubPullRequestInfo(number, url, headRef, state);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task CreateOrUpdateLabelAsync(string label, string color, string description, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { name = label, color, description });
        using var createResponse = await httpClient.PostAsync($"repos/{repository}/labels", Content(payload), cancellationToken);
        if (createResponse.IsSuccessStatusCode)
        {
            return;
        }

        if (createResponse.StatusCode != System.Net.HttpStatusCode.UnprocessableEntity)
        {
            await EnsureSuccessAsync(createResponse);
        }

        await SendAsync(HttpMethod.Patch, $"repos/{repository}/labels/{Uri.EscapeDataString(label)}", payload, cancellationToken);
    }

    private async Task SendAsync(HttpMethod method, string path, string payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = Content(payload) };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    private static StringContent Content(string payload)
    {
        return new StringContent(payload, Encoding.UTF8, "application/json");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"GitHub API call failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }
}
