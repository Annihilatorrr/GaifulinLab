using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class RegistrationTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task Registration_ValidatesFieldsAndRecoversFromRateLimitAndNetworkFailure()
    {
        var email = $"registration-{Guid.NewGuid():N}@example.com";
        const string displayName = "Registration Test";
        const string password = "Strong-password-1!";
        await Page.GotoAsync(new Uri(environment.BaseUri, "/register").ToString());

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.Locator(".validation-message")).ToHaveCountAsync(4);

        await Page.GetByLabel("Display name").FillAsync(new string('a', 101));
        await Page.GetByLabel("Email").FillAsync("not-an-email");
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync("short");
        await Page.GetByLabel("Confirm password").FillAsync("different");
        Assert.False(await Page.Locator("#registration-email").EvaluateAsync<bool>("input => input.validity.valid"));
        Assert.NotEmpty(await Page.Locator("#registration-email").EvaluateAsync<string>("input => input.validationMessage"));
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByText("Display name must be between 2 and 100 characters.")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Passwords do not match.")).ToBeVisibleAsync();

        await Page.GetByLabel("Display name").FillAsync(displayName);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await Page.GetByLabel("Confirm password").FillAsync(password);
        await Page.RouteAsync("**/api/auth/register", route => route.FulfillAsync(new() { Status = 429 }));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Too many registration attempts");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Create account" })).ToBeEnabledAsync();
        await Page.UnrouteAsync("**/api/auth/register");

        await Page.RouteAsync("**/api/auth/register", route => route.AbortAsync());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Unable to reach the server");
        await Expect(Page.GetByLabel("Display name")).ToHaveValueAsync(displayName);
        await Expect(Page.GetByLabel("Email")).ToHaveValueAsync(email);
        await Expect(Page.GetByLabel("Password", new() { Exact = true })).ToHaveValueAsync(password);
        await Page.UnrouteAsync("**/api/auth/register");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Welcome, {displayName}" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Registration_ReportsDuplicateAndWeakPasswordBeforeSuccessfulRegistration()
    {
        var duplicateEmail = $"duplicate-{Guid.NewGuid():N}@example.com";
        const string password = "Strong-password-1!";
        await RegisterAsync(duplicateEmail, "First Account", password);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/register").ToString());
        await FillRegistrationAsync(duplicateEmail, "Second Account", password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("already in use");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Create an account" })).ToBeVisibleAsync();

        var weakEmail = $"weak-{Guid.NewGuid():N}@example.com";
        await Page.GotoAsync(new Uri(environment.BaseUri, "/register").ToString());
        await FillRegistrationAsync(weakEmail, "Weak Password", "alllowercase1!");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Email")).ToHaveValueAsync(weakEmail);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await Page.GetByLabel("Confirm password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome, Weak Password" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Login_RejectsInvalidAndLockedAccountsAndRecoversAfterNetworkFailure()
    {
        var email = $"login-{Guid.NewGuid():N}@example.com";
        const string password = "Strong-password-1!";
        await RegisterAsync(email, "Login Test", password);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(email);
        await Page.Locator("#admin-password").FillAsync("incorrect-password");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Invalid login or password.");
        await Expect(Page.Locator("#admin-password")).ToHaveValueAsync(string.Empty);
        Assert.DoesNotContain("/admin/articles", Page.Url);

        await Page.Locator("#admin-password").FillAsync(password);
        await Page.RouteAsync("**/api/auth/login", route => route.AbortAsync());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Unable to reach the server.");
        await Page.UnrouteAsync("**/api/auth/login");
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToBeVisibleAsync();

        var lockedEmail = $"locked-{Guid.NewGuid():N}@example.com";
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await RegisterAsync(lockedEmail, "Locked Test", password);
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Page.Locator("#admin-login").FillAsync(lockedEmail);
            await Page.Locator("#admin-password").FillAsync("incorrect-password");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Invalid login or password.");
        }

        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Invalid login or password.");
        Assert.DoesNotContain("/admin/articles", Page.Url);
    }

    [Fact]
    public async Task GuestsAreRedirectedAndAuthorsSeeOnlyTheirOwnDrafts()
    {
        foreach (var path in new[]
                 {
                     "/admin/articles", "/admin/articles/new", $"/admin/articles/{Guid.NewGuid()}", "/admin/profile"
                 })
        {
            await Page.GotoAsync(new Uri(environment.BaseUri, path).ToString());
            await Expect(Page).ToHaveURLAsync(new Regex("/admin/login$"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToHaveCountAsync(0);
        }

        const string password = "Strong-password-1!";
        var ownerEmail = $"owner-{Guid.NewGuid():N}@example.com";
        var otherEmail = $"other-{Guid.NewGuid():N}@example.com";
        var ownerTitle = "Owner-only draft " + Guid.NewGuid().ToString("N");
        var otherTitle = "Other-author draft " + Guid.NewGuid().ToString("N");
        await RegisterAsync(ownerEmail, "Owner", password);
        await SignInAsync(ownerEmail, password);
        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync(ownerTitle);
        await Page.GetByLabel("Article Markdown").FillAsync("A private draft.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();

        await RegisterAsync(otherEmail, "Other", password);
        await SignInAsync(otherEmail, password);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync(otherTitle);
        await Page.GetByLabel("Article Markdown").FillAsync("Another private draft.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles").ToString());
        await Expect(Page.GetByText(ownerTitle, new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByText(otherTitle, new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OtherAuthorCannotReadOrMutateAnArticleThroughItsEditorUrl()
    {
        const string password = "Strong-password-1!";
        var ownerEmail = $"editor-owner-{Guid.NewGuid():N}@example.com";
        var otherEmail = $"editor-other-{Guid.NewGuid():N}@example.com";
        var title = "Owner editor title " + Guid.NewGuid().ToString("N");
        var markdown = "Original private article body.";
        await RegisterAsync(ownerEmail, "Editor Owner", password);
        await SignInAsync(ownerEmail, password);
        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync(title);
        await Page.GetByLabel("Article Markdown").FillAsync(markdown);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/articles/[0-9a-f-]{36}$"));
        var editorUrl = Page.Url;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();

        await RegisterAsync(otherEmail, "Editor Other", password);
        await SignInAsync(otherEmail, password);
        await Page.GotoAsync(editorUrl);
        await Expect(Page.GetByText(title, new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Page.GetByLabel("Article title").FillAsync("Attempted overwrite");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Page.GetByLabel("More article actions").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.GetByLabel("More article actions").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete article", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await SignInAsync(ownerEmail, password);
        await Page.GotoAsync(editorUrl);
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync(title);
        await Expect(Page.GetByLabel("Article Markdown")).ToHaveValueAsync(markdown);
    }

    [Fact]
    public async Task AuthenticatedSessionSurvivesReloadButSignOutAndUnauthorizedSaveReturnToLogin()
    {
        const string password = "Strong-password-1!";
        var email = $"session-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email, "Session User", password);
        await SignInAsync(email, password);
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Profile" })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync("Unauthorized save");
        await Page.GetByLabel("Article Markdown").FillAsync("This change must not be saved.");
        await Page.RouteAsync("**/api/admin/articles", route => route.FulfillAsync(new() { Status = 401 }));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/login$"));
        await Expect(Page.GetByText(new Regex("Saved|All changes saved"))).ToHaveCountAsync(0);
        await Page.UnrouteAsync("**/api/admin/articles");

        await SignInAsync(email, password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/login$"));
        await Page.GoBackAsync();
        await Page.ReloadAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/login$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My articles" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ProfileValidatesRecoversFromFailuresAndUpdatesPublicAuthorName()
    {
        const string password = "Strong-password-1!";
        var email = $"profile-{Guid.NewGuid():N}@example.com";
        var articleTitle = "Profile author article " + Guid.NewGuid().ToString("N");
        var slug = "profile-author-" + Guid.NewGuid().ToString("N");
        var series = await environment.SeedSeriesAsync(
            "Profile series " + Guid.NewGuid().ToString("N"),
            "profile-series-" + Guid.NewGuid().ToString("N"),
            null);
        await RegisterAsync(email, "Original Name", password);
        await SignInAsync(email, password);
        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Page.GetByLabel("Article title").FillAsync(articleTitle);
        await Page.GetByPlaceholder("article-slug").FillAsync(slug);
        await Page.GetByLabel("Article Markdown").FillAsync("A published article keeps the current author name live.");
        await Page.GetByText("+ Add series", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel(series.Title, new() { Exact = true }).CheckAsync();
        await Page.GetByLabel("Position in series", new() { Exact = true }).FillAsync("1");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/profile").ToString());
        await Page.RouteAsync("**/api/auth/profile", route => route.AbortAsync());
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Unable to load your profile");
        await Page.UnrouteAsync("**/api/auth/profile");
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Display name")).ToHaveValueAsync("Original Name");

        foreach (var invalidName in new[] { "", "   ", "A", new string('a', 101) })
        {
            await Page.GetByLabel("Display name").FillAsync(invalidName);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save profile" }).ClickAsync();
            await Expect(Page.Locator(".validation-message")).ToBeVisibleAsync();
        }
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Display name")).ToHaveValueAsync("Original Name");

        const string updatedName = "Updated Profile Name";
        await Page.GetByLabel("Display name").FillAsync(updatedName);
        await Page.RouteAsync("**/api/auth/profile", route => route.FulfillAsync(new() { Status = 500 }));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save profile" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Unable to save your profile");
        await Page.UnrouteAsync("**/api/auth/profile");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save profile" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Profile saved");
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Display name")).ToHaveValueAsync(updatedName);

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/articles/{slug}").ToString());
        await Expect(Page.GetByText($"By {updatedName}")).ToBeVisibleAsync();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/articles").ToString());
        await Expect(Page.Locator(".content-byline")).ToContainTextAsync(updatedName);
        await Page.GotoAsync(environment.BaseUri.ToString());
        await Expect(Page.Locator(".overview-column").First).ToContainTextAsync(updatedName);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/en/series/{series.Slug}").ToString());
        await Expect(Page.Locator(".series-list")).ToContainTextAsync($"By {updatedName}");
    }

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

    private async Task RegisterAsync(string email, string displayName, string password)
    {
        await Page.GotoAsync(new Uri(environment.BaseUri, "/register").ToString());
        await FillRegistrationAsync(email, displayName, password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Welcome, {displayName}" })).ToBeVisibleAsync();
    }

    private async Task FillRegistrationAsync(string email, string displayName, string password)
    {
        await Page.GetByLabel("Display name").FillAsync(displayName);
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await Page.GetByLabel("Confirm password").FillAsync(password);
    }

    private async Task SignInAsync(string login, string password)
    {
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }
}
