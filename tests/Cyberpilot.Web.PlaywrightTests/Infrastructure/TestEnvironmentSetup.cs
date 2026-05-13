using System;
using NUnit.Framework;

namespace Cyberpilot.Web.PlaywrightTests
{
    [SetUpFixture]
    public class TestEnvironmentSetup
    {
        [OneTimeSetUp]
        public void GlobalSetup()
        {
            try
            {
                var b = Environment.GetEnvironmentVariable("BROWSER");
                if (!string.IsNullOrEmpty(b))
                {
                    // Accept only the canonical browser names used by Playwright.
                    var normalized = b.Trim().ToLowerInvariant();
                    if (normalized != "chromium" && normalized != "firefox" && normalized != "webkit")
                    {
                        // Override any odd BROWSER value coming from the environment (e.g. VS Code helper scripts)
                        Environment.SetEnvironmentVariable("BROWSER", "chromium");
                    }
                }
            }
            catch
            {
                // Keep setup robust: if anything goes wrong here, let tests continue and fail naturally.
            }
        }
    }
}
