using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class RegistrationTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task VisitorRegisters_AndAdministratorPublishesArticle_ThatIsPubliclyAvailable()
    {
        const string password = "Strong-password-1!";
        var login = $"e2e-user-{Guid.NewGuid():N}";
        var uniqueId = Guid.NewGuid().ToString("N");
        var title = $"E2E registration article {uniqueId}";
        var slug = $"e2e-registration-article-{uniqueId}";
        var body = $"Registration workflow verification {uniqueId}.";
        var summary = $"Article summary {uniqueId}.";
        var tag = $"e2e-tag-{uniqueId}";
        var topic = await environment.SeedTopicAsync(
            $"E2E topic {uniqueId}",
            $"e2e-topic-{uniqueId}",
            $"Topic description {uniqueId}.");

        await Page.GotoAsync(new Uri(environment.BaseUri, "/register").ToString());

        await Page.GetByLabel("Login").FillAsync(login);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await Page.GetByLabel("Confirm password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Welcome, {login}" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText("Workspace access is granted separately"))
            .ToBeVisibleAsync();

        await SignInAsync(login, password);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Articles could not be loaded" }))
            .ToBeVisibleAsync();

        await Page.EvaluateAsync("sessionStorage.clear()");
        var (adminLogin, adminPassword) = environment.GetAdminCredentials();
        await SignInAsync(adminLogin, adminPassword);
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles$"));

        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync(title);
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync(slug);
        await Page.GetByPlaceholder("Add summary…").FillAsync(summary);
        await Page.GetByLabel("Tags").FillAsync($"{tag}, {tag.ToUpperInvariant()}");
        await Page.GetByText("+ Add topic", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel(topic.Name, new() { Exact = true }).CheckAsync();
        await Page.GetByLabel("Article Markdown").FillAsync(body);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/articles/{slug}").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
        await Expect(Page.GetByText(summary, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("article.article-body")).ToContainTextAsync(body);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/topics").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = topic.Name })).ToBeVisibleAsync();
        await Expect(Page.GetByText(topic.Description!, new() { Exact = true })).ToBeVisibleAsync();

        var browserMessages = new List<string>();
        Page.PageError += (_, error) => browserMessages.Add($"page error: {error}");
        Page.Console += (_, message) => browserMessages.Add($"{message.Type}: {message.Text}");
        await Page.GotoAsync(new Uri(
            environment.BaseUri,
            $"/articles?topic={topic.Slug}&tag={tag}").ToString());
        await Page.WaitForTimeoutAsync(250);
        if (await Page.Locator("#blazor-error-ui").IsVisibleAsync())
        {
            Assert.Fail($"The filtered public articles page failed:\n{string.Join('\n', browserMessages)}");
        }
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = title })).ToBeVisibleAsync();
        await Expect(Page.Locator(".metadata")).ToContainTextAsync(tag);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/tags").ToString());
        await Expect(Page.GetByText($"#{tag}")).ToBeVisibleAsync();
    }

    private async Task SignInAsync(string login, string password)
    {
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }
}
