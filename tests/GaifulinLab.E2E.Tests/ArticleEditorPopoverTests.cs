using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleEditorPopoverTests : PageTest
{
    private readonly E2EEnvironment _environment;

    public ArticleEditorPopoverTests(E2EEnvironment environment)
    {
        _environment = environment;
    }

    [Fact]
    public async Task MetadataAndActionPopovers_CloseWhenClickingOutside()
    {
        var (login, password) = _environment.GetAdminCredentials();

        await Page.GotoAsync(new Uri(_environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();

        var popovers = Page.Locator("details[data-dismiss-on-outside-click]");
        await Expect(popovers).ToHaveCountAsync(3);

        for (var index = 0; index < await popovers.CountAsync(); index++)
        {
            var popover = popovers.Nth(index);
            await popover.Locator("summary").ClickAsync();
            await Expect(popover).ToHaveAttributeAsync("open", "");

            await Page.GetByLabel("Article title").ClickAsync();
            await Expect(popover).Not.ToHaveAttributeAsync("open", "");
        }
    }

    [Fact]
    public async Task Popovers_DoNotMoveTheEditorWorkspace()
    {
        var (login, password) = _environment.GetAdminCredentials();

        await Page.GotoAsync(new Uri(_environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();

        var workspace = Page.Locator(".editor-workspace");
        var initialTop = await TopAsync(workspace);
        var popovers = Page.Locator("details[data-dismiss-on-outside-click]");

        for (var index = 0; index < await popovers.CountAsync(); index++)
        {
            var popover = popovers.Nth(index);
            await popover.Locator("summary").ClickAsync();
            await Expect(popover).ToHaveAttributeAsync("open", "");
            await AssertWorkspaceTopAsync(workspace, initialTop);

            await Page.GetByLabel("Article title").ClickAsync();
        }

        var tags = Page.GetByLabel("Tags");
        await tags.FillAsync("layout-stability");
        await Expect(tags).ToHaveValueAsync("layout-stability");
    }

    private static async Task<double> TopAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync();
        return Assert.IsType<LocatorBoundingBoxResult>(box).Y;
    }

    private static async Task AssertWorkspaceTopAsync(ILocator workspace, double expectedTop)
    {
        var actualTop = await TopAsync(workspace);
        Assert.InRange(Math.Abs(actualTop - expectedTop), 0, 0.5);
    }
}
