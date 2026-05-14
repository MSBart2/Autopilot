using Cyberpilot.Web.Services;

namespace Cyberpilot.Web.UnitTests.Services;

public sealed class RepositoryProfileDetectorTests
{
    [Fact]
    public async Task DetectAsync_DotNetSolutionAndDocs_ReturnsBuildTestAndDocumentationSignals()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "App.sln"), string.Empty);
        File.WriteAllText(Path.Combine(tempDir.Path, "README.md"), string.Empty);
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "docs"));
        var detector = new RepositoryProfileDetector();

        var profile = await detector.DetectAsync(tempDir.Path);

        Assert.Equal<string>([".NET"], profile.Languages);
        Assert.Equal<string>(["dotnet build ./App.sln"], profile.BuildCommands);
        Assert.Equal<string>(["dotnet test ./App.sln"], profile.TestCommands);
        Assert.Equal<string>(["README.md", "docs/"], profile.DocumentationPaths);
        Assert.Contains("languages: .NET", profile.ToSummary());
    }

    [Fact]
    public async Task DetectAsync_NodePackageScripts_ReturnsNpmSignals()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "package.json"), "{\"scripts\":{\"build\":\"vite build\",\"test\":\"vitest\"}}");
        var detector = new RepositoryProfileDetector();

        var profile = await detector.DetectAsync(tempDir.Path);

        Assert.Equal<string>(["Node.js"], profile.Languages);
        Assert.Equal<string>(["npm run build"], profile.BuildCommands);
        Assert.Equal<string>(["npm test"], profile.TestCommands);
    }

    [Fact]
    public async Task DetectAsync_NoSignals_ReturnsEmptyProfile()
    {
        using var tempDir = new TempDirectory();
        var detector = new RepositoryProfileDetector();

        var profile = await detector.DetectAsync(tempDir.Path);

        Assert.False(profile.HasSignals);
        Assert.Contains("no build, test, or documentation conventions", profile.ToSummary());
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
}