using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleEditConflictTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task StaleEditorSave_PreservesTheDraftAndRequiresAnExplicitResolution()
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var article = await environment.SeedPublishedArticleAsync(
            "Original title",
            $"edit-conflict-{uniqueId}",
            "# Original content");
        var (login, password) = environment.GetAdminCredentials();

        await SignInAsync(login, password);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("Original title");

        await environment.ChangeArticleLocalizationAsync(
            article.Id,
            "en",
            "Saved in another editor",
            "# Saved elsewhere");

        await Page.GetByLabel("Article title").FillAsync("My unsaved draft");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

        var conflict = Page.Locator(".editor-conflict");
        await Expect(conflict.GetByRole(AriaRole.Heading, new()
        {
            Name = "This localization was changed elsewhere"
        })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("My unsaved draft");
        await Expect(conflict.Locator("input")).ToHaveValueAsync("Saved in another editor");

        await conflict.GetByRole(AriaRole.Button, new() { Name = "Save my draft", Exact = true }).ClickAsync();
        await Expect(conflict).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();

        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("My unsaved draft");
    }

    private async Task SignInAsync(string login, string password)
    {
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToBeVisibleAsync();
    }
}
