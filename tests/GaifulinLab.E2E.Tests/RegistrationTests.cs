using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class RegistrationTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task VisitorRegisters_AndPublishesArticle_ThatIsPubliclyAvailable()
    {
        const string password = "Strong-password-1!";
        const string displayName = "E2E Author";
        const string topicName = "Digital Signal Processing";
        const string topicSlug = "digital-signal-processing";
        const string topicDescription = "Signals, filters, spectral analysis, and practical DSP.";
        var login = $"e2e-user-{Guid.NewGuid():N}@example.com";
        var uniqueId = Guid.NewGuid().ToString("N");
        var title = $"E2E registration article {uniqueId}";
        var slug = $"e2e-registration-article-{uniqueId}";
        var body = $"Registration workflow verification {uniqueId}.";
        var summary = $"Article summary {uniqueId}.";
        var tag = $"e2e-tag-{uniqueId}";
        await Page.GotoAsync(new Uri(environment.BaseUri, "/register").ToString());

        await Page.GetByLabel("Display name").FillAsync(displayName);
        await Page.GetByLabel("Email").FillAsync(login);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await Page.GetByLabel("Confirm password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Welcome, {displayName}" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText("Your account is ready. Sign in to create and manage your own articles."))
            .ToBeVisibleAsync();

        await SignInAsync(login, password);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" }))
            .ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync(title);
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync(slug);
        await Page.GetByPlaceholder("Add summary…").FillAsync(summary);
        await Page.GetByLabel("Tags").FillAsync($"{tag}, {tag.ToUpperInvariant()}");
        await Page.GetByText("+ Add topic", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel(topicName, new() { Exact = true }).CheckAsync();
        await Page.GetByLabel("Article Markdown").FillAsync(body);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        var articleId = ExtractArticleId();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/articles/{slug}").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
        await Expect(Page.GetByText($"By {displayName}", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText(summary, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("article.article-body")).ToContainTextAsync(body);
        await Expect(Page.Locator(".article-views")).ToContainTextAsync("1 views");
        await Expect(Page.GetByText(new Regex("^Last edited "))).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, "/topics").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = topicName })).ToBeVisibleAsync();
        await Expect(Page.GetByText(topicDescription, new() { Exact = true })).ToBeVisibleAsync();

        var browserMessages = new List<string>();
        Page.PageError += (_, error) => browserMessages.Add($"page error: {error}");
        Page.Console += (_, message) => browserMessages.Add($"{message.Type}: {message.Text}");
        await Page.GotoAsync(new Uri(
            environment.BaseUri,
            $"/articles?topic={topicSlug}&tag={tag}").ToString());
        await Page.WaitForTimeoutAsync(250);
        if (await Page.Locator("#blazor-error-ui").IsVisibleAsync())
        {
            Assert.Fail($"The filtered public articles page failed:\n{string.Join('\n', browserMessages)}");
        }
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = title })).ToBeVisibleAsync();
        await Expect(Page.Locator(".metadata")).ToContainTextAsync(tag);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/tags").ToString());
        await Expect(Page.GetByText($"#{tag}")).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{articleId}").ToString());
        await Page.GetByLabel("More article actions").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unpublish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Unpublished");

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/articles/{slug}").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Article not found" })).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{articleId}").ToString());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");

        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.GetByLabel("More article actions").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete article", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles$"));

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/articles/{slug}").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Article not found" })).ToBeVisibleAsync();
    }

    private string ExtractArticleId() => new Uri(Page.Url).Segments[^1].Trim('/');

    private async Task SignInAsync(string login, string password)
    {
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }
}
