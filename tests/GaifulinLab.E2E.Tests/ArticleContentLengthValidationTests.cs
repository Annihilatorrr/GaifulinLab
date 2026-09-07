using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleContentLengthValidationTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task SavingAnOverlongTitle_ShowsValidationErrorAndDoesNotCreateArticle()
    {
        var (login, password) = environment.GetAdminCredentials();

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();

        await Page.GetByLabel("Article title").FillAsync(new string('t', 301));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("title must not exceed 300 characters");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/articles/new$"));
    }
}
