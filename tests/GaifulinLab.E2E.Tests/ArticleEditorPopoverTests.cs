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
        await Expect(popovers).ToHaveCountAsync(4);

        for (var index = 0; index < await popovers.CountAsync(); index++)
        {
            var popover = popovers.Nth(index);
            await popover.Locator("summary").ClickAsync();
            await Expect(popover).ToHaveAttributeAsync("open", "");

            await Page.GetByLabel("Article title").ClickAsync();
            await Expect(popover).Not.ToHaveAttributeAsync("open", "");
        }
    }
}
