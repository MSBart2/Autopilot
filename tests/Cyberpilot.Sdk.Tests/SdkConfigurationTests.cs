using Cyberpilot.Options;

namespace Cyberpilot.Sdk.Tests;

public sealed class SdkConfigurationTests
{
    [Fact]
    public void Load_WithExplicitAppsettingsFile_UsesMatchingRepositoryToken()
    {
        using var tempDir = new TempDirectory();
        var configPath = Path.Combine(tempDir.Path, "appsettings.json");
        File.WriteAllText(configPath,
            """
            {
              "Cyberpilot": {
                "Repository": "owner/repo",
                "Repositories": [
                  {
                    "Name": "Primary",
                    "Repository": "https://github.com/owner/repo",
                    "RepoRoot": "C:/Repos/Repo",
                    "Token": "configured-token"
                  }
                ]
              }
            }
            """);

        var configuration = SdkConfiguration.Load(configPath, tempDir.Path);

        Assert.Equal("owner/repo", configuration.DefaultRepository);
        Assert.Equal("configured-token", configuration.GetToken("owner/repo"));
        Assert.Equal("configured-token", configuration.GetToken("https://github.com/owner/repo.git"));
    }

    [Fact]
    public void ApplyTo_WhenRepoIsMissing_UsesConfiguredDefaultRepository()
    {
        using var tempDir = new TempDirectory();
        var configPath = Path.Combine(tempDir.Path, "appsettings.json");
        File.WriteAllText(configPath,
            """
            {
              "Cyberpilot": {
                "Repository": "owner/repo",
                "Repositories": [
                  {
                    "Name": "Primary",
                    "Repository": "owner/repo",
                    "RepoRoot": "C:/Repos/Repo",
                    "Token": "configured-token"
                  }
                ]
              }
            }
            """);
        var options = new CyberpilotOptions(42, tempDir.Path, null, CyberpilotOptions.DefaultModel, false, false, false, false, CyberpilotOptions.DefaultStageTimeout, false, false, null, configPath, false);

        var resolved = SdkConfiguration.Load(configPath, tempDir.Path).ApplyTo(options);

        Assert.Equal("owner/repo", resolved.Repository);
        Assert.Equal(Path.GetFullPath("C:/Repos/Repo"), resolved.RepoRoot);
    }

    [Fact]
    public void ApplyTo_WhenRepoIsProvided_UsesMatchingConfiguredRepoRoot()
    {
        using var tempDir = new TempDirectory();
        var configPath = Path.Combine(tempDir.Path, "appsettings.json");
        File.WriteAllText(configPath,
            """
            {
              "Cyberpilot": {
                "Repositories": [
                  {
                    "Name": "Primary",
                    "Repository": "owner/repo",
                    "RepoRoot": "C:/Repos/Repo",
                    "Token": "configured-token"
                  }
                ]
              }
            }
            """);
        var options = new CyberpilotOptions(42, tempDir.Path, "owner/repo", CyberpilotOptions.DefaultModel, false, false, false, false, CyberpilotOptions.DefaultStageTimeout, false, false, null, configPath, false);

        var resolved = SdkConfiguration.Load(configPath, tempDir.Path).ApplyTo(options);

        Assert.Equal("owner/repo", resolved.Repository);
        Assert.Equal(Path.GetFullPath("C:/Repos/Repo"), resolved.RepoRoot);
    }

    [Fact]
    public void ApplyTo_WhenRepoIsProvided_DoesNotOverrideRepository()
    {
        using var tempDir = new TempDirectory();
        var configPath = Path.Combine(tempDir.Path, "appsettings.json");
        File.WriteAllText(configPath,
            """
            {
              "Cyberpilot": {
                "Repository": "owner/repo"
              }
            }
            """);
        var options = new CyberpilotOptions(42, tempDir.Path, "other/repo", CyberpilotOptions.DefaultModel, false, false, false, false, CyberpilotOptions.DefaultStageTimeout, false, false, null, configPath, false);

        var resolved = SdkConfiguration.Load(configPath, tempDir.Path).ApplyTo(options);

        Assert.Equal("other/repo", resolved.Repository);
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
