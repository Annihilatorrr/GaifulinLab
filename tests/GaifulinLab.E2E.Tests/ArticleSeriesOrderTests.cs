using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleSeriesOrderTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task DeletingTheSecondArticle_AllowsAReplacementAtTheSamePosition()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var series = await environment.SeedSeriesAsync(
            "Reading order",
            $"reading-order-{suffix}",
            null);
        var (login, password) = environment.GetAdminCredentials();

        await SignInAsync(login, password);
        var first = await CreateAndPublishArticleAsync("First lesson", $"first-lesson-{suffix}", series.Title, 1);
        var second = await CreateAndPublishArticleAsync("Second lesson", $"second-lesson-{suffix}", series.Title, 2);
        await CreateAndPublishArticleAsync("Third lesson", $"third-lesson-{suffix}", series.Title, 3);
        await CreateAndPublishArticleAsync("Fourth lesson", $"fourth-lesson-{suffix}", series.Title, 4);

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{second.Id}").ToString());
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.GetByLabel("More article actions").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete article", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles$"));

        await CreateAndPublishArticleAsync("New second lesson", $"new-second-lesson-{suffix}", series.Title, 2);

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/series/{series.Slug}").ToString());
        var titles = Page.Locator(".series-list li strong");
        await Expect(titles).ToHaveCountAsync(4);
        Assert.Equal(
            ["First lesson", "New second lesson", "Third lesson", "Fourth lesson"],
            await titles.AllInnerTextsAsync());
    }

    private async Task<CreatedArticle> CreateAndPublishArticleAsync(
        string title,
        string slug,
        string seriesTitle,
        int position)
    {
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article title").FillAsync(title);
        await Page.GetByPlaceholder("article-slug").FillAsync(slug);
        await Page.GetByLabel("Article Markdown").FillAsync($"# {title}");

        await Page.GetByText("+ Add series", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel(seriesTitle, new() { Exact = true }).CheckAsync();
        var positionField = Page.GetByLabel("Position in series", new() { Exact = true });
        await positionField.FillAsync(position.ToString());
        await positionField.PressAsync("Tab");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        var articleId = new Uri(Page.Url).Segments[^1].Trim('/');
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");

        return new CreatedArticle(articleId);
    }

    private async Task SignInAsync(string login, string password)
    {
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles$"));
    }

    private sealed record CreatedArticle(string Id);
}
