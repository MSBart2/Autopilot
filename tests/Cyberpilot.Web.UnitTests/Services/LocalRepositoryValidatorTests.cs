using Cyberpilot.Web.Services;
using System.Diagnostics;

namespace Cyberpilot.Web.UnitTests.Services;

public sealed class LocalRepositoryValidatorTests
{
    [Fact]
    public void CyberpilotWebOptions_DefaultsEnsureLabelsToTrue()
    {
        var options = new CyberpilotWebOptions();

        Assert.True(options.EnsureLabels);
    }

    [Fact]
    public async Task ValidateAsync_WithNullRepoRoot_ThrowsArgumentNullException()
    {
        var validator = new LocalRepositoryValidator();

        await Assert.ThrowsAsync<ArgumentNullException>(() => validator.ValidateAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WithBlankRepoRoot_ThrowsArgumentException(string repoRoot)
    {
        var validator = new LocalRepositoryValidator();

        await Assert.ThrowsAsync<ArgumentException>(() => validator.ValidateAsync(repoRoot!));
    }

    [Fact]
    public async Task ValidateAsync_WithMissingRepoRoot_ThrowsDirectoryNotFoundException()
    {
        var validator = new LocalRepositoryValidator();
        var missingRepoRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => validator.ValidateAsync(missingRepoRoot));
    }

    [Fact]
    public async Task ValidateAsync_WithNonGitDirectory_ThrowsInvalidOperationException()
    {
        if (!GitIsAvailable())
        {
            return;
        }

        using var tempDir = new TempDirectory();
        var validator = new LocalRepositoryValidator();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(tempDir.Path));

        Assert.Contains("not a git work tree", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_WithGitRepository_ReturnsFullPath()
    {
        if (!GitIsAvailable())
        {
            return;
        }

        using var tempDir = new TempDirectory();
        var repoRoot = Path.Combine(tempDir.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        RunGit(repoRoot, "init");

        var validator = new LocalRepositoryValidator();
        var result = await validator.ValidateAsync(repoRoot);

        Assert.Equal(Path.GetFullPath(repoRoot), result);
    }

    [Fact]
    public async Task PrepareAsync_WithExistingRepoRoot_ValidatesWithoutCloning()
    {
        using var tempDir = new TempDirectory();
        var fakeGit = new FakeGitCommandRunner();
        var validator = new LocalRepositoryValidator(fakeGit);

        var result = await validator.PrepareAsync(tempDir.Path, "owner/repo", "token-value");

        var fullPath = Path.GetFullPath(tempDir.Path);
        Assert.Equal(fullPath, result);
        Assert.Equal(2, fakeGit.Calls.Count);
        Assert.Equal(fullPath, fakeGit.Calls[0].WorkingDirectory);
        Assert.Equal(["rev-parse", "--is-inside-work-tree"], fakeGit.Calls[0].Args);
        Assert.Null(fakeGit.Calls[0].GitHubToken);
        Assert.Equal(fullPath, fakeGit.Calls[1].WorkingDirectory);
        Assert.Equal(["status", "--porcelain"], fakeGit.Calls[1].Args);
        Assert.Null(fakeGit.Calls[1].GitHubToken);
    }

    [Fact]
    public async Task PrepareAsync_WithMissingRepoRoot_ClonesAndValidates()
    {
        using var tempDir = new TempDirectory();
        var repoRoot = Path.Combine(tempDir.Path, "repo");
        var fakeGit = new FakeGitCommandRunner(call =>
        {
            if (call.Args.Count == 3 && call.Args[0] == "clone")
            {
                Directory.CreateDirectory(call.Args[2]);
            }

            return new GitCommandResult(0, string.Empty, string.Empty);
        });
        var validator = new LocalRepositoryValidator(fakeGit);

        var result = await validator.PrepareAsync(repoRoot, "owner/repo", "token-value");

        var fullRepoRoot = Path.GetFullPath(repoRoot);
        Assert.Equal(fullRepoRoot, result);
        Assert.Equal(3, fakeGit.Calls.Count);
        Assert.Equal(Path.GetFullPath(tempDir.Path), fakeGit.Calls[0].WorkingDirectory);
        Assert.Equal(["clone", "https://github.com/owner/repo.git", fullRepoRoot], fakeGit.Calls[0].Args);
        Assert.Equal("token-value", fakeGit.Calls[0].GitHubToken);
        Assert.Equal(fullRepoRoot, fakeGit.Calls[1].WorkingDirectory);
        Assert.Equal(["rev-parse", "--is-inside-work-tree"], fakeGit.Calls[1].Args);
        Assert.Equal(fullRepoRoot, fakeGit.Calls[2].WorkingDirectory);
        Assert.Equal(["status", "--porcelain"], fakeGit.Calls[2].Args);
    }

    [Fact]
    public async Task PrepareAsync_WithCloneFailure_RedactsTokenFromException()
    {
        using var tempDir = new TempDirectory();
        var repoRoot = Path.Combine(tempDir.Path, "repo");
        var fakeGit = new FakeGitCommandRunner(_ => new GitCommandResult(128, string.Empty, "authentication failed for token secret-token"));
        var validator = new LocalRepositoryValidator(fakeGit);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.PrepareAsync(repoRoot, "owner/repo", "secret-token"));

        Assert.Contains("Failed to clone owner/repo", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token", ex.Message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", ex.Message, StringComparison.Ordinal);
    }

    private static bool GitIsAvailable()
    {
        try
        {
            RunGit(Directory.GetCurrentDirectory(), "--version");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(error);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class FakeGitCommandRunner : IGitCommandRunner
    {
        private readonly Func<GitCommandCall, GitCommandResult> handler;

        public FakeGitCommandRunner()
            : this(_ => new GitCommandResult(0, string.Empty, string.Empty))
        {
        }

        public FakeGitCommandRunner(Func<GitCommandCall, GitCommandResult> handler)
        {
            this.handler = handler;
        }

        public List<GitCommandCall> Calls { get; } = [];

        public Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, string? githubToken = null, CancellationToken cancellationToken = default)
        {
            var call = new GitCommandCall(Path.GetFullPath(workingDirectory), args.ToArray(), githubToken);
            Calls.Add(call);
            return Task.FromResult(handler(call));
        }
    }

    private sealed record GitCommandCall(string WorkingDirectory, IReadOnlyList<string> Args, string? GitHubToken);
}