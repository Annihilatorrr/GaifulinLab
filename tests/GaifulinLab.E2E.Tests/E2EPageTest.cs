using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

public abstract class E2EPageTest : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new() { Locale = "en-US" };
}
