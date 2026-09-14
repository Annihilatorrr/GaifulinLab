using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleEditorScrollTests : E2EPageTest
{
    private readonly E2EEnvironment _environment;

    public ArticleEditorScrollTests(E2EEnvironment environment)
    {
        _environment = environment;
    }

    [Fact]
    public async Task HtmlAndPreviewScrollIndependently()
    {
        var (login, password) = _environment.GetAdminCredentials();

        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync(new Uri(_environment.BaseUri, "/admin/login").ToString());
        await Page.Locator("#admin-login").FillAsync(login);
        await Page.Locator("#admin-password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();

        var html = Page.Locator("#article-html");
        var preview = Page.Locator(".preview-pane");
        var previewHeading = preview.Locator(".pane-heading");
        var articlePreview = Page.Locator("article.article-preview");
        await html.FillAsync($"<p>Before thematic break</p><hr>{CreateLongHtml()}");
        await Expect(articlePreview).ToContainTextAsync("Paragraph 140");

        // The preview gutter and article must form one continuous surface.
        Assert.Equal(
            await BackgroundColorAsync(articlePreview),
            await BackgroundColorAsync(preview));

        // Native HTML thematic breaks remain visible in the editor preview.
        await Expect(articlePreview.Locator("hr")).ToBeVisibleAsync();

        await AssertScrollableAsync(html);
        await AssertScrollableAsync(preview);

        var pageScrollBeforePreview = await Page.EvaluateAsync<double>("() => window.scrollY");
        await ScrollWithMouseAsync(preview);
        var previewScrollTop = await ScrollTopAsync(preview);
        var pageScrollAfterPreview = await Page.EvaluateAsync<double>("() => window.scrollY");

        Assert.True(previewScrollTop > 0, "The preview did not scroll after a mouse-wheel event.");
        Assert.Equal(pageScrollBeforePreview, pageScrollAfterPreview);
        await AssertHeadingStaysStickyAsync(preview, previewHeading);
        await AssertPreviewDoesNotScrollThePageAtItsBoundaryAsync(preview);

        var previewScrollBeforeHtml = await ScrollTopAsync(preview);
        await ScrollWithMouseAsync(html);
        var htmlScrollTop = await ScrollTopAsync(html);
        var previewScrollAfterHtml = await ScrollTopAsync(preview);

        Assert.True(htmlScrollTop > 0, "The Html editor did not scroll after a mouse-wheel event.");
        Assert.Equal(previewScrollBeforeHtml, previewScrollAfterHtml);

        var settings = Page.Locator(".preview-settings");
        await settings.Locator("summary").ClickAsync();
        await Expect(settings).ToHaveAttributeAsync("open", "");

        var splitter = Page.Locator(".editor-splitter");
        await splitter.FocusAsync();
        await splitter.PressAsync("Home");
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "30");
        await splitter.PressAsync("ArrowRight");
        await Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "35");
    }

    private static string CreateLongHtml() => string.Join(
        "\n\n",
        Enumerable.Range(1, 140).Select(number => $"<p>Paragraph {number}</p>"));

    private static async Task AssertScrollableAsync(ILocator locator)
    {
        await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var hasVerticalOverflow = await locator.EvaluateAsync<bool>(
            "element => element.scrollHeight > element.clientHeight");

        Assert.True(
            hasVerticalOverflow,
            $"Expected {await locator.GetAttributeAsync("class") ?? locator.ToString()} to have vertical overflow.");
    }

    private async Task ScrollWithMouseAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync();
        var bounds = Assert.IsType<LocatorBoundingBoxResult>(box);

        await Page.Mouse.MoveAsync(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        await Page.Mouse.WheelAsync(0, 600);
        await Page.WaitForFunctionAsync("element => element.scrollTop > 0", await locator.ElementHandleAsync());
    }

    private static async Task AssertHeadingStaysStickyAsync(ILocator preview, ILocator heading)
    {
        var previewBox = Assert.IsType<LocatorBoundingBoxResult>(await preview.BoundingBoxAsync());
        var headingBox = Assert.IsType<LocatorBoundingBoxResult>(await heading.BoundingBoxAsync());

        Assert.InRange(Math.Abs(previewBox.Y - headingBox.Y), 0, 0.5);
    }

    private async Task AssertPreviewDoesNotScrollThePageAtItsBoundaryAsync(ILocator preview)
    {
        await preview.EvaluateAsync("element => element.scrollTop = 0");
        var pageScrollBeforeTopBoundary = await Page.EvaluateAsync<double>("() => window.scrollY");
        await ScrollWheelAsync(preview, -600);
        await Page.WaitForTimeoutAsync(100);
        var pageScrollAfterTopBoundary = await Page.EvaluateAsync<double>("() => window.scrollY");

        Assert.Equal(pageScrollBeforeTopBoundary, pageScrollAfterTopBoundary);
        Assert.Equal(0, await ScrollTopAsync(preview));

        await preview.EvaluateAsync("element => element.scrollTop = element.scrollHeight");
        var pageScrollBefore = await Page.EvaluateAsync<double>("() => window.scrollY");
        await ScrollWheelAsync(preview, 600);
        await Page.WaitForTimeoutAsync(100);
        var pageScrollAfter = await Page.EvaluateAsync<double>("() => window.scrollY");

        Assert.Equal(pageScrollBefore, pageScrollAfter);
    }

    private async Task ScrollWheelAsync(ILocator locator, float deltaY)
    {
        var box = await locator.BoundingBoxAsync();
        var bounds = Assert.IsType<LocatorBoundingBoxResult>(box);

        await Page.Mouse.MoveAsync(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        await Page.Mouse.WheelAsync(0, deltaY);
    }

    private static Task<double> ScrollTopAsync(ILocator locator) =>
        locator.EvaluateAsync<double>("element => element.scrollTop");

    private static Task<string> BackgroundColorAsync(ILocator locator) =>
        locator.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor");
}
