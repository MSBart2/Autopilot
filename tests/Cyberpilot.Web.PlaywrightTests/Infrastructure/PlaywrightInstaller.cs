using System;
using System.Threading.Tasks;

namespace Cyberpilot.Web.PlaywrightTests.Infrastructure;

internal static class PlaywrightInstaller
{
    private static bool _installed;

    public static async Task EnsureBrowsersAsync()
    {
        if (_installed)
        {
            return;
        }

        // Ensure the Playwright browser binaries are available before tests run.
        var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(new[] { "install" })).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Playwright browser installation failed with exit code {exitCode}.");
        }
        _installed = true;
    }
}
