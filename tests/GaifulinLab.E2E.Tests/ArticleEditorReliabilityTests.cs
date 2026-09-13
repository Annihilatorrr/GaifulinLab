using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleEditorReliabilityTests(E2EEnvironment environment) : PageTest
{
    [Fact]
    public async Task ListDeleteRetryAndReload_OnlyRemovesTheRequestedArticle()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var deleteFails = true;
        var deleted = false;
        await AuthenticateAsync();
        await Page.RouteAsync("**/*", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath.TrimEnd('/');
            if (!path.StartsWith("/api/admin/", StringComparison.Ordinal))
            {
                await route.ContinueAsync();
                return;
            }
            if (path == "/api/admin/articles" && route.Request.Method == "GET")
            {
                var items = deleted
                    ? new[] { ListItem(second, "Keep this article", 0) }
                    : new[] { ListItem(first, "Delete this article", 0), ListItem(second, "Keep this article", 0) };
                await JsonAsync(route, items);
                return;
            }
            if (path == $"/api/admin/articles/{first}" && route.Request.Method == "DELETE")
            {
                if (deleteFails) { await route.FulfillAsync(new() { Status = 500 }); return; }
                deleted = true;
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }
            await JsonAsync(route, new { });
        });

        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles").ToString());
        await Expect(Page.Locator(".article-row")).ToHaveCountAsync(2);
        await Page.Locator(".article-row").Filter(new() { HasText = "Delete this article" }).GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Expect(Page.GetByText("Delete this article", new() { Exact = true })).ToBeVisibleAsync();

        deleteFails = false;
        await Page.Locator(".article-row").Filter(new() { HasText = "Delete this article" }).GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Expect(Page.GetByText("Delete this article", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Keep this article", new() { Exact = true })).ToBeVisibleAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByText("Delete this article", new() { Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ListLoadRetryAndMissingEditor_ShowErrorsWithoutFalseSuccess()
    {
        var loaded = false;
        await AuthenticateAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath.TrimEnd('/');
            if (path == "/api/admin/articles" && route.Request.Method == "GET")
            {
                if (!loaded) { await route.FulfillAsync(new() { Status = 500 }); return; }
                await JsonAsync(route, new[] { ListItem(Guid.NewGuid(), "Loaded after retry", 0) });
                return;
            }
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            await route.FulfillAsync(new() { Status = 404, ContentType = "application/json", Body = "{}" });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Articles could not be loaded" })).ToBeVisibleAsync();
        loaded = true;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await Expect(Page.GetByText("Loaded after retry", new() { Exact = true })).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{Guid.NewGuid()}").ToString());
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Page.GetByLabel("Article title").FillAsync("Must not look saved");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Expect(Page.GetByText(new System.Text.RegularExpressions.Regex("Saved|All changes saved"))).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task EditorCancelDeleteAndRetryAfterDeleteFailure_KeepOrRemoveTheArticleAsExpected()
    {
        Page.SetDefaultTimeout(5_000);
        var article = new MockArticle();
        var allowDelete = false;
        var acceptDelete = false;
        await AuthenticateAsync();
        await RouteEditorAsync(article, route => new Uri(route.Request.Url).AbsolutePath == $"/api/admin/articles/{article.Id}" && route.Request.Method == "DELETE" && !allowDelete ? 500 : null);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync(article.Title);

        Page.Dialog += async (_, dialog) =>
        {
            if (acceptDelete) await dialog.AcceptAsync();
            else await dialog.DismissAsync();
        };
        await OpenActionsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete article" }).ClickAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync(article.Title);

        acceptDelete = true;
        await OpenActionsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete article" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync(article.Html);

        allowDelete = true;
        await OpenActionsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete article" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/articles$"));
    }

    [Fact]
    public async Task ManualAndAutomaticSavesPersistLatestEditsWithoutCreatingNewDrafts()
    {
        Page.SetDefaultTimeout(5_000);
        var article = new MockArticle { Status = 1 };
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync(article.Html);
        await Page.GetByLabel("Article title").FillAsync("Updated title");
        await Page.Locator("textarea.summary-field").FillAsync("Updated summary");
        await Page.GetByLabel("Article Html").FillAsync("Updated body");
        await Page.GetByPlaceholder("article-slug").FillAsync("updated-slug");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("Updated title");
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Updated body");
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync("updated-slug");

        await Page.GetByLabel("Article Html").FillAsync("Autosaved final body");
        await Page.WaitForTimeoutAsync(2200);
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Autosaved final body");

        var creates = 0;
        await Page.UnrouteAsync("**/api/admin/**");
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath.TrimEnd('/');
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/articles" && route.Request.Method == "POST") creates++;
            if (path == "/api/admin/html/preview") { await JsonAsync(route, new { html = "" }); return; }
            await JsonAsync(route, new { });
        });
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article title").FillAsync("Unsaved new article");
        await Page.GetByLabel("Article Html").FillAsync("No automatic create");
        await Page.WaitForTimeoutAsync(1600);
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles").ToString());
        Assert.Equal(0, creates);
    }

    [Fact]
    public async Task FailedSaveKeepsTheDraftAndRetryPersistsIt()
    {
        var article = new MockArticle();
        var failSave = true;
        await AuthenticateAsync();
        await RouteEditorAsync(article, route =>
            new Uri(route.Request.Url).AbsolutePath.Contains($"/api/admin/articles/{article.Id}/localizations/en", StringComparison.Ordinal)
            && route.Request.Method == "PUT" && failSave ? 500 : null);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Page.GetByLabel("Article Html").FillAsync("Draft retained after failure");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page.GetByText("Save failed", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Draft retained after failure");

        failSave = false;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Draft retained after failure");
    }

    [Fact]
    public async Task TypingDuringDelayedSave_QueuesTheLatestRevision()
    {
        var article = new MockArticle();
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = 0;
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.RouteAsync($"**/api/admin/articles/{article.Id}/localizations/en", async route =>
        {
            if (route.Request.Method != "PUT") { await route.ContinueAsync(); return; }
            saves++;
            using var request = JsonDocument.Parse(route.Request.PostData!);
            var html = request.RootElement.GetProperty("html").GetString()!;
            if (saves == 1)
            {
                firstSaveStarted.TrySetResult();
                await releaseFirstSave.Task;
            }
            article.Html = html;
            article.Version++;
            await JsonAsync(route, article.Version);
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync(article.Html);
        await Page.GetByLabel("Article Html").FillAsync("First revision");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Page.GetByLabel("Article Html").FillAsync("Latest revision");
        releaseFirstSave.TrySetResult();
        await Page.WaitForTimeoutAsync(2200);
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Latest revision");
        Assert.True(saves >= 2);
    }

    [Fact]
    public async Task AutosaveDuringAFailedCoverUpload_PreservesTheTextDraft()
    {
        Page.SetDefaultTimeout(5_000);
        var article = MultiArticle.WithEnglishDraft();
        var coverUploadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failCoverUpload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.RouteAsync("**/api/admin/media", async route =>
        {
            coverUploadStarted.TrySetResult();
            await failCoverUpload.Task;
            await ErrorAsync(route, 500, "upload_failed", "The cover could not be uploaded.");
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Page.GetByText("+ Add cover", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel("Upload cover", new() { Exact = true }).SetInputFilesAsync(new FilePayload
        {
            Name = "cover.png",
            MimeType = "image/png",
            Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")
        });
        await coverUploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Keep the upload busy past the autosave delay; a failed upload will not mark the draft dirty again.
        var saveResponse = Page.WaitForResponseAsync(response =>
            response.Request.Method == "PUT"
            && new Uri(response.Url).AbsolutePath == $"/api/admin/articles/{article.Id}/localizations/en");
        await Page.GetByLabel("Article Html").FillAsync("Text saved while cover upload is pending");
        Assert.Equal(200, (await saveResponse).Status);
        Assert.Equal("Text saved while cover upload is pending", article.Localizations["en"].Html);
        failCoverUpload.TrySetResult();
        await Expect(Page.Locator(".cover-setting [role=alert]")).ToHaveTextAsync("The cover could not be uploaded.");

        // The text must survive reload without a manual save or a successful upload scheduling another save.
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Text saved while cover upload is pending");
    }

    [Fact]
    public async Task SwitchingLanguagesDuringAutosave_DoesNotApplyAStaleReloadOrShowAConflict()
    {
        Page.SetDefaultTimeout(5_000);
        var article = MultiArticle.WithEnglishAndRussian();
        var russianSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRussianSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var articleGets = 0;
        var saveRequests = new List<(string Language, long? ExpectedVersion)>();
        var activeLocalizationSaves = 0;
        var maximumActiveLocalizationSaves = 0;
        var englishSaveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.RouteAsync($"**/api/admin/articles/{article.Id}", async route =>
        {
            if (route.Request.Method != "GET")
            {
                await route.ContinueAsync();
                return;
            }

            articleGets++;
            if (articleGets > 1)
            {
                reloadStarted.TrySetResult();
                await releaseReload.Task;
            }

            await JsonAsync(route, MultiDetails(article));
        });
        await Page.RouteAsync($"**/api/admin/articles/{article.Id}/localizations/*", async route =>
        {
            if (route.Request.Method != "PUT")
            {
                await route.ContinueAsync();
                return;
            }

            activeLocalizationSaves++;
            maximumActiveLocalizationSaves = Math.Max(maximumActiveLocalizationSaves, activeLocalizationSaves);
            try
            {
                var language = new Uri(route.Request.Url).Segments[^1].TrimEnd('/');
                using var request = JsonDocument.Parse(route.Request.PostData!);
                var expectedVersion = request.RootElement.GetProperty("expectedVersion").GetInt64();
                saveRequests.Add((language, expectedVersion));
                var current = article.Localizations[language];
                article.Localizations[language] = new MultiLocalization(
                    current.Id,
                    current.Version + 1,
                    current.Language,
                    request.RootElement.GetProperty("slug").GetString() ?? "",
                    request.RootElement.GetProperty("title").GetString() ?? "",
                    request.RootElement.GetProperty("summary").GetString() ?? "",
                    request.RootElement.GetProperty("html").GetString() ?? "",
                    current.Status);
                if (language == "ru")
                {
                    russianSaveStarted.TrySetResult();
                    await releaseRussianSave.Task;
                }

                await JsonAsync(route, article.Localizations[language].Version);
                if (language == "en")
                {
                    englishSaveCompleted.TrySetResult();
                }
            }
            finally
            {
                activeLocalizationSaves--;
            }
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Page.GetByLabel("Article Html").FillAsync("Russian autosave snapshot");
        await Page.GetByLabel("Article language").SelectOptionAsync("en");

        await russianSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseRussianSave.TrySetResult();
        await reloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The GET started with the old English value; this edit must survive its late response.
        await Page.GetByLabel("Article Html").FillAsync("English edit after reload began");
        releaseReload.TrySetResult();

        await englishSaveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("English edit after reload began");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true })).ToBeEnabledAsync();

        // Switching languages must neither overlap saves nor manufacture a version conflict.
        await Expect(Page.Locator(".editor-conflict")).ToHaveCountAsync(0);
        Assert.Equal(1, maximumActiveLocalizationSaves);
        Assert.Equal([("ru", (long?)5), ("en", (long?)1)], saveRequests);

        // Both language snapshots must reach persistence despite the late reload.
        Assert.Equal(6, article.Localizations["ru"].Version);
        Assert.Equal("Russian autosave snapshot", article.Localizations["ru"].Html);
        Assert.Equal(2, article.Localizations["en"].Version);
        Assert.Equal("English edit after reload began", article.Localizations["en"].Html);
    }

    [Fact]
    public async Task SwitchingLanguagesDuringDelayedPreview_DoesNotCrashOrPublishAStalePreview()
    {
        Page.SetDefaultTimeout(5_000);
        var article = MultiArticle.WithEnglishAndRussian();
        var russianPreviewStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentEnglishPreviewStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCurrentEnglishPreview = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageErrors = new List<string>();
        Page.PageError += (_, error) => pageErrors.Add(error);
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.RouteAsync("**/api/admin/html/preview", async route =>
        {
            using var request = JsonDocument.Parse(route.Request.PostData!);
            var html = request.RootElement.GetProperty("html").GetString();
            if (html == "Russian preview")
            {
                russianPreviewStarted.TrySetResult();
            }
            else if (html == "Current English preview")
            {
                currentEnglishPreviewStarted.TrySetResult();
                await releaseCurrentEnglishPreview.Task;
            }

            await JsonAsync(route, new { html = $"<p>{html}</p>" });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        var preview = Page.Locator("article.article-preview");
        await Expect(preview).ToContainTextAsync("English Html");
        await Page.EvaluateAsync("""
            () => {
              const originalClearMath = window.articleAssets.clearMath;
              window.previewClearGate = { started: false, release: null };
              window.articleAssets.clearMath = async root => {
                if (!window.previewClearGate.started) {
                  window.previewClearGate.started = true;
                  await new Promise(resolve => window.previewClearGate.release = resolve);
                }

                return originalClearMath(root);
              };
            }
            """);

        await Page.GetByLabel("Article Html").FillAsync("Current English preview");
        await Page.WaitForFunctionAsync("() => window.previewClearGate.started");

        // The first English refresh is waiting in clearMath when the RU refresh cancels it.
        // A final EN refresh must win after the stale continuation is released.
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Page.GetByLabel("Article Html").FillAsync("Russian preview");
        await russianPreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Page.GetByLabel("Article language").SelectOptionAsync("en");
        await currentEnglishPreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Page.EvaluateAsync("() => window.previewClearGate.release()");
        releaseCurrentEnglishPreview.TrySetResult();

        // The cancelled English continuation must not overwrite the latest preview.
        await Expect(preview).ToHaveTextAsync("Current English preview");

        // Cancellation during JavaScript interop must leave the renderer usable.
        await Expect(Page.Locator(".preview-error")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.Empty(pageErrors);
    }

    [Fact]
    public async Task SwitchingToAMissingLanguageDuringDelayedSave_QueuesTheNewLocalization()
    {
        Page.SetDefaultTimeout(5_000);
        var article = MultiArticle.WithEnglishDraft();
        var englishSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEnglishSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.RouteAsync($"**/api/admin/articles/{article.Id}/localizations/en", async route =>
        {
            if (route.Request.Method != "PUT")
            {
                await route.ContinueAsync();
                return;
            }

            englishSaveStarted.TrySetResult();
            await releaseEnglishSave.Task;
            await route.FallbackAsync();
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Page.GetByLabel("Article Html").FillAsync("English snapshot");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await englishSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Programmatic change exercises the component path even though the busy UI disables the selector.
        await Page.GetByLabel("Article language").EvaluateAsync("select => { select.value = 'ru'; select.dispatchEvent(new Event('change', { bubbles: true })); }");
        await Page.GetByLabel("Article Html").FillAsync("New Russian draft");
        var russianSaveResponse = Page.WaitForResponseAsync(response =>
            response.Request.Method == "PUT"
            && new Uri(response.Url).AbsolutePath == $"/api/admin/articles/{article.Id}/localizations/ru");
        releaseEnglishSave.TrySetResult();

        // Background autosave does not itself render the status; use its response as the persistence signal.
        Assert.Equal(200, (await russianSaveResponse).Status);
        // The pending autosave must preserve both drafts after a language event adds a localization.
        await Expect(Page.Locator(".editor-conflict")).ToHaveCountAsync(0);
        Assert.Equal(2, article.Localizations["en"].Version);
        Assert.Equal(1, article.Localizations["ru"].Version);
        Assert.Equal("English snapshot", article.Localizations["en"].Html);
        Assert.Equal("New Russian draft", article.Localizations["ru"].Html);

        // Reload without another manual save to prove both snapshots were already persisted.
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("English snapshot");
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("New Russian draft");
    }

    [Fact]
    public async Task SwitchingToAnEmptyTranslationAndBack_DoesNotReuseADisposedPreviewCancellation()
    {
        var article = MultiArticle.WithEnglishDraft();
        var pageErrors = new List<string>();
        Page.PageError += (_, error) => pageErrors.Add(error);
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        var preview = Page.Locator("article.article-preview");
        await Expect(preview).ToContainTextAsync("English body");

        // The empty translation takes the early-return path that used to leave a disposed source behind.
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("");
        await Expect(preview).ToHaveCountAsync(0);
        await Page.GetByLabel("Article language").SelectOptionAsync("en");

        // Returning to a populated localization must restore preview without crashing Blazor.
        await Expect(preview).ToContainTextAsync("English body");
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.Empty(pageErrors);
    }

    [Fact]
    public async Task RetryAfterTaxonomyFailure_CompletesTheSameNewArticleWithoutCreatingADuplicate()
    {
        var article = new MockArticle();
        var createRequests = 0;
        var taxonomyRequests = 0;
        var taxonomySaved = false;
        await AuthenticateAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview") { await JsonAsync(route, new { html = "<p>Preview</p>" }); return; }
            if (path == "/api/admin/articles" && route.Request.Method == "POST")
            {
                createRequests++;
                ApplyCreateRequest(article, route.Request.PostData!);
                await JsonAsync(route, new { articleId = article.Id, localizationId = article.LocalizationId, localizationVersion = article.Version }, 201);
                return;
            }
            if (path == $"/api/admin/articles/{article.Id}/taxonomy" && route.Request.Method == "PUT")
            {
                taxonomyRequests++;
                if (taxonomyRequests == 1)
                {
                    await ErrorAsync(route, 500, "taxonomy_save_failed", "Article metadata could not be saved.");
                    return;
                }

                using var request = JsonDocument.Parse(route.Request.PostData!);
                article.Tags = request.RootElement.GetProperty("tags").EnumerateArray()
                    .Select(tag => tag.GetString() ?? "")
                    .Where(tag => tag.Length > 0)
                    .ToArray();
                taxonomySaved = article.Tags.Contains("part-one", StringComparer.Ordinal);
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }
            if (path == $"/api/admin/articles/{article.Id}" && route.Request.Method == "GET") { await JsonAsync(route, Details(article)); return; }
            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article title").FillAsync("One article, two save attempts");
        await Page.GetByLabel("Article Html").FillAsync("Body retained across the retry");
        await Page.GetByPlaceholder("Separate tags with commas").FillAsync("part-one");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

        await Expect(Page.GetByText("Save failed", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Article metadata could not be saved.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Save failed", new() { Exact = true })).ToHaveCountAsync(0);

        Assert.Equal(1, createRequests);
        Assert.Equal(2, taxonomyRequests);
        Assert.True(taxonomySaved);
        Assert.Equal("Body retained across the retry", article.Html);
        await Expect(Page.GetByPlaceholder("Separate tags with commas")).ToHaveValueAsync("part-one");
    }

    [Fact]
    public async Task PublishNewArticleWithoutSave_CreatesItOnceAndExposesTheLatestContent()
    {
        var article = new MockArticle();
        var createRequests = 0;
        var publishRequests = 0;
        await AuthenticateAsync();
        await Page.RouteAsync("**/*", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview")
            {
                using var preview = JsonDocument.Parse(route.Request.PostData!);
                await JsonAsync(route, new { html = $"<p>{preview.RootElement.GetProperty("html").GetString()}</p>" });
                return;
            }
            if (path == "/api/admin/articles" && route.Request.Method == "POST")
            {
                createRequests++;
                ApplyCreateRequest(article, route.Request.PostData!);
                await JsonAsync(route, new { articleId = article.Id, localizationId = article.LocalizationId, localizationVersion = article.Version }, 201);
                return;
            }
            if (path == $"/api/admin/articles/{article.Id}" && route.Request.Method == "GET") { await JsonAsync(route, Details(article)); return; }
            if (path == $"/api/admin/articles/{article.Id}/localizations/en/publish" && route.Request.Method == "POST")
            {
                publishRequests++;
                article.Status = 1;
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }
            if (path == "/api/public/articles/en/publish-without-save" && route.Request.Method == "GET")
            {
                if (article.Status != 1) { await route.FulfillAsync(new() { Status = 404 }); return; }
                await JsonAsync(route, PublicDetails(article));
                return;
            }
            if (path == "/api/public/articles/en/publish-without-save/views" && route.Request.Method == "POST")
            {
                await JsonAsync(route, new { viewCount = 1L });
                return;
            }
            await route.ContinueAsync();
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article title").FillAsync("Publish without save");
        await Page.Locator("textarea.summary-field").FillAsync("The newest summary");
        await Page.GetByLabel("Article Html").FillAsync("The newest body");
        await Page.GetByPlaceholder("article-slug").FillAsync("publish-without-save");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();

        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/admin/articles/{article.Id}$"));
        Assert.Equal(1, createRequests);
        Assert.Equal(1, publishRequests);
        Assert.Equal("The newest body", article.Html);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/publish-without-save").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Publish without save" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".article-body")).ToContainTextAsync("The newest body");
    }

    [Fact]
    public async Task FailedPublishAndUnpublish_KeepTheServerStatusAndCanBeRetried()
    {
        var article = new MockArticle();
        var failPublish = true;
        var failUnpublish = true;
        await AuthenticateAsync();
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview") { await JsonAsync(route, new { html = "<p>Preview</p>" }); return; }
            if (path == $"/api/admin/articles/{article.Id}" && route.Request.Method == "GET") { await JsonAsync(route, Details(article)); return; }
            if (path == $"/api/admin/articles/{article.Id}/localizations/en/publish" && route.Request.Method == "POST")
            {
                if (failPublish) { await ErrorAsync(route, 500, "publish_failed", "Publishing is temporarily unavailable."); return; }
                article.Status = 1;
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }
            if (path == $"/api/admin/articles/{article.Id}/localizations/en/unpublish" && route.Request.Method == "POST")
            {
                if (failUnpublish) { await ErrorAsync(route, 500, "unpublish_failed", "Unpublishing is temporarily unavailable."); return; }
                article.Status = 2;
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }
            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Publishing is temporarily unavailable.");
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Draft");
        Assert.Equal(0, article.Status);

        failPublish = false;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");
        Assert.Equal(1, article.Status);

        await OpenActionsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unpublish", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Unpublishing is temporarily unavailable.");
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");
        Assert.Equal(1, article.Status);

        failUnpublish = false;
        await OpenActionsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unpublish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Unpublished");
        Assert.Equal(2, article.Status);
    }

    [Fact]
    public async Task AutomaticSlug_FollowsEnglishAndRussianTitlesInEachNewLocalization()
    {
        await AuthenticateAsync();
        await RouteNewEditorAsync();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());

        await Page.GetByLabel("Article title").FillAsync("Test Html → PDF");
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync("test-html-pdf");
        await Page.GetByLabel("Article title").FillAsync("Café déjà vu");
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync("cafe-deja-vu");

        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Page.GetByLabel("Article title").FillAsync("Почему дисперсию делят на n-1");
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync("pochemu-dispersiyu-delyat-na-n-1");
        await Page.GetByLabel("Article title").FillAsync("Ёжик, чай и щука");
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync("yozhik-chay-i-shchuka");
    }

    [Fact]
    public async Task ManualSlug_SurvivesTitleChangesSaveAndReloadForPublishedArticle()
    {
        var article = new MockArticle { Status = 1, Title = "Published original", Slug = "stable-public-url" };
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());

        await Page.GetByLabel("Article title").FillAsync("Title changed before save");
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync("stable-public-url");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync("stable-public-url");

        await Page.GetByLabel("Article title").FillAsync("Title changed after reload");
        await Expect(Page.GetByPlaceholder("article-slug")).ToHaveValueAsync("stable-public-url");
        await OpenActionsAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Open article", Exact = true })).ToHaveAttributeAsync("href", "/en/articles/stable-public-url");
    }

    [Fact]
    public async Task ConflictUseSavedVersion_ReplacesAllDraftFieldsAndPreviewWithTheLatestServerVersion()
    {
        var article = new MockArticle();
        var firstSaveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AuthenticateAsync();
        await RouteConflictEditorAsync(Page, article, firstSaveCompleted);
        var otherPage = await Context.NewPageAsync();
        await AuthenticateAsync(otherPage);
        await RouteConflictEditorAsync(otherPage, article);

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await otherPage.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("Original title");
        await Expect(otherPage.GetByLabel("Article title")).ToHaveValueAsync("Original title");

        await Page.GetByLabel("Article title").FillAsync("Saved in the first tab");
        await Page.Locator("textarea.summary-field").FillAsync("Server summary");
        await Page.GetByLabel("Article Html").FillAsync("Server body");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await firstSaveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await otherPage.GetByLabel("Article title").FillAsync("Stale local title");
        await otherPage.Locator("textarea.summary-field").FillAsync("Stale local summary");
        await otherPage.GetByLabel("Article Html").FillAsync("Stale local body");
        await otherPage.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        var conflict = otherPage.Locator(".editor-conflict");
        await Expect(conflict).ToBeVisibleAsync();
        await Expect(otherPage.GetByLabel("Article title")).ToHaveValueAsync("Stale local title");

        await conflict.GetByRole(AriaRole.Button, new() { Name = "Use saved version", Exact = true }).ClickAsync();
        await Expect(conflict).ToBeHiddenAsync();
        await Expect(otherPage.GetByLabel("Article title")).ToHaveValueAsync("Saved in the first tab");
        await Expect(otherPage.Locator("textarea.summary-field")).ToHaveValueAsync("Server summary");
        await Expect(otherPage.GetByLabel("Article Html")).ToHaveValueAsync("Server body");
        await Expect(otherPage.Locator(".article-preview")).ToContainTextAsync("Server body");

        await otherPage.CloseAsync();
    }

    [Fact]
    public async Task ConflictSaveMyDraft_PersistsEveryFieldWithoutChangingTheOtherTranslation()
    {
        var article = MultiArticle.WithEnglishAndRussian();
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("English original");

        article.Localizations["en"] = article.Localizations["en"] with
        {
            Version = 2,
            Title = "Server title",
            Summary = "Server summary",
            Html = "Server Html"
        };
        article.Localizations["ru"] = article.Localizations["ru"] with
        {
            Version = 6,
            Title = "Русский серверный апдейт",
            Html = "Обновлено независимо"
        };
        await Page.GetByLabel("Article title").FillAsync("My complete draft");
        await Page.Locator("textarea.summary-field").FillAsync("My draft summary");
        await Page.GetByLabel("Article Html").FillAsync("My draft Html");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

        var conflict = Page.Locator(".editor-conflict");
        await Expect(conflict).ToBeVisibleAsync();
        await conflict.GetByRole(AriaRole.Button, new() { Name = "Save my draft", Exact = true }).ClickAsync();
        await Expect(conflict).ToBeHiddenAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("My complete draft");
        await Expect(Page.Locator("textarea.summary-field")).ToHaveValueAsync("My draft summary");
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("My draft Html");

        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("Русский серверный апдейт");
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Обновлено независимо");
    }

    [Fact]
    public async Task ConflictSaveMyDraft_WhenServerChangesAgain_ShowsANewConflict()
    {
        var article = MultiArticle.WithEnglishAndRussian();
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        // A disabled Save also appears during loading; wait until this editor has captured version one.
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("English original");

        article.Localizations["en"] = article.Localizations["en"] with { Version = 2, Title = "Server version two" };
        await Page.GetByLabel("Article title").FillAsync("My stale draft");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        var conflict = Page.Locator(".editor-conflict");
        await Expect(conflict).ToBeVisibleAsync();
        await Expect(conflict.Locator("input")).ToHaveValueAsync("Server version two");

        article.Localizations["en"] = article.Localizations["en"] with { Version = 3, Title = "Server version three" };
        article.ForceConflicts = true;
        await conflict.GetByRole(AriaRole.Button, new() { Name = "Save my draft", Exact = true }).ClickAsync();
        await Expect(conflict).ToBeVisibleAsync();
        await Expect(conflict.Locator("input")).ToHaveValueAsync("Server version three");
        Assert.Equal("Server version three", article.Localizations["en"].Title);
    }

    [Fact]
    public async Task ConflictServerVersionLoadFailure_KeepsLocalTextAndCanBeRetried()
    {
        var article = MultiArticle.WithEnglishAndRussian();
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("English Html");

        article.Localizations["en"] = article.Localizations["en"] with { Version = 2, Title = "Latest server title" };
        article.FailConflictLoad = true;
        await Page.GetByLabel("Article title").FillAsync("Local text must survive");
        await Page.GetByLabel("Article Html").FillAsync("Local Html must survive");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("latest version could not be loaded");
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("Local text must survive");
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Local Html must survive");
        await Expect(Page.Locator(".editor-conflict")).ToHaveCountAsync(0);

        article.FailConflictLoad = false;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".editor-conflict")).ToBeVisibleAsync();
        await Page.Locator(".editor-conflict").GetByRole(AriaRole.Button, new() { Name = "Use saved version", Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("Latest server title");
    }

    [Fact]
    public async Task NewRussianArticle_PublishesDirectlyAsRussianAndOpensAtItsRussianUrl()
    {
        var article = new MultiArticle();
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await RouteMultiPublicAsync(Page, article);
        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());

        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Page.GetByLabel("Article title").FillAsync("Русская статья");
        await Page.Locator("textarea.summary-field").FillAsync("Русское описание");
        await Page.GetByLabel("Article Html").FillAsync("Последний русский текст");
        await Page.GetByPlaceholder("article-slug").FillAsync("russkaya-statya");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/admin/articles/{article.Id}$"));
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");
        var russian = Assert.Single(article.Localizations);
        Assert.Equal("ru", russian.Key);
        Assert.Equal("Последний русский текст", russian.Value.Html);
        Assert.Equal(1, russian.Value.Status);

        await Page.GotoAsync(new Uri(environment.BaseUri, "/ru/articles/russkaya-statya").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Русская статья" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".article-body")).ToContainTextAsync("Последний русский текст");
    }

    [Fact]
    public async Task TranslationEditsStayIndependentAcrossSwitchesReloadAndPublicationChanges()
    {
        var article = MultiArticle.WithEnglishDraft();
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await RouteMultiPublicAsync(Page, article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());

        await Page.GetByLabel("Article title").FillAsync("English edited");
        await Page.Locator("textarea.summary-field").FillAsync("English edited summary");
        await Page.GetByLabel("Article Html").FillAsync("English edited body");
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Page.GetByLabel("Article title").FillAsync("Русский перевод");
        await Page.Locator("textarea.summary-field").FillAsync("Русское описание");
        await Page.GetByLabel("Article Html").FillAsync("Русский текст");
        await Page.GetByPlaceholder("article-slug").FillAsync("russkiy-perevod");
        await Page.GetByLabel("Article language").SelectOptionAsync("en");
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("English edited");
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Русский текст");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("English edited");
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Expect(Page.GetByLabel("Article title")).ToHaveValueAsync("Русский перевод");
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("Русский текст");

        await Page.GetByLabel("Article language").SelectOptionAsync("en");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");
        Assert.Equal(1, article.Localizations["en"].Status);
        Assert.Equal(0, article.Localizations["ru"].Status);
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/english-edited").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "English edited" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Available localizations" })).ToHaveCountAsync(0);

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Page.GetByLabel("Article language").SelectOptionAsync("ru");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".publication-status")).ToHaveTextAsync("Published");
        await Page.GotoAsync(new Uri(environment.BaseUri, "/ru/articles/russkiy-perevod").ToString());
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Available localizations" })).ToContainTextAsync("EN");

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await OpenActionsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unpublish", Exact = true }).ClickAsync();
        Assert.Equal(2, article.Localizations["en"].Status);
        await Page.GotoAsync(new Uri(environment.BaseUri, "/en/articles/english-edited").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Article not found" })).ToBeVisibleAsync();
        await Page.GotoAsync(new Uri(environment.BaseUri, "/ru/articles/russkiy-perevod").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Русский перевод" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TaxonomyAssignmentsAndNormalizedTags_PersistAfterReloadAndCanBeCleared()
    {
        var article = MultiArticle.WithEnglishDraft();
        article.Taxonomy = TestTaxonomy.Create();
        await AuthenticateAsync();
        await RouteMultiEditorAsync(Page, article);
        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        await Expect(Page.GetByLabel("Article Html")).ToHaveValueAsync("English body");

        await Page.GetByText("+ Add topic", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel("Engineering", new() { Exact = true }).CheckAsync();
        await Page.GetByLabel("Signal processing", new() { Exact = true }).CheckAsync();
        await Page.GetByText("+ Add series", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel("Series One", new() { Exact = true }).CheckAsync();
        await Page.GetByLabel("Series Two", new() { Exact = true }).CheckAsync();
        var positions = Page.GetByLabel("Position in series", new() { Exact = true });
        await positions.Nth(0).FillAsync("2");
        await positions.Nth(0).PressAsync("Tab");
        await positions.Nth(1).FillAsync("4");
        await positions.Nth(1).PressAsync("Tab");
        await Page.GetByPlaceholder("Separate tags with commas").FillAsync("test, TEST, test, , another");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();

        Assert.Equal(2, article.TopicIds.Length);
        Assert.Equal(2, article.SeriesAssignments.Count);
        Assert.Equal(2, article.SeriesAssignments[article.Taxonomy.Series[0].Id]);
        Assert.Equal(4, article.SeriesAssignments[article.Taxonomy.Series[1].Id]);
        Assert.Equal(["test", "another"], article.Tags);
        await Page.ReloadAsync();
        await Expect(Page.GetByText("Topics (2)", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Series (2)", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByPlaceholder("Separate tags with commas")).ToHaveValueAsync("test, another");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove tag test", Exact = true }).ClickAsync();
        await Expect(Page.GetByPlaceholder("Separate tags with commas")).ToHaveValueAsync("another");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove topic Engineering", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove topic Signal processing", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove series Series One", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove series Series Two", Exact = true }).ClickAsync();
        await Page.GetByPlaceholder("Separate tags with commas").FillAsync("");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Page.ReloadAsync();

        await Expect(Page.GetByText("+ Add topic", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("+ Add series", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByPlaceholder("Separate tags with commas")).ToHaveValueAsync("");
        Assert.Empty(article.TopicIds);
        Assert.Empty(article.SeriesAssignments);
        Assert.Empty(article.Tags);
    }

    [Fact]
    public async Task EditorPdfExport_RequiresExplicitSaveUsesCurrentTypographyAndPreventsDuplicates()
    {
        Page.SetDefaultTimeout(5_000);
        var article = new MockArticle();
        var exportId = Guid.NewGuid();
        var releaseExport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exportStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveRequests = 0;
        var exportRequests = 0;
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.RouteAsync($"**/api/admin/articles/en/{article.Slug}/pdf-exports**", async route =>
        {
            exportRequests++;
            Assert.Equal("POST", route.Request.Method);
            var query = new Uri(route.Request.Url).Query;
            Assert.Contains("lineHeight=2", query, StringComparison.Ordinal);
            Assert.Contains("blockSpacing=1.1", query, StringComparison.Ordinal);
            exportStarted.TrySetResult();
            await releaseExport.Task;
            await JsonAsync(route, new
            {
                id = exportId,
                status = "completed",
                errorMessage = (string?)null,
                downloadUrl = $"/api/admin/pdf-exports/{exportId}/download"
            }, 202);
        });
        await Page.RouteAsync($"**/api/admin/pdf-exports/{exportId}/download", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/pdf",
            BodyBytes = [37, 80, 68, 70, 45, 49, 46, 52]
        }));
        await Page.RouteAsync($"**/api/admin/articles/{article.Id}/localizations/en", async route =>
        {
            if (route.Request.Method != "PUT")
            {
                await route.ContinueAsync();
                return;
            }

            saveRequests++;
            using var request = JsonDocument.Parse(route.Request.PostData!);
            ApplyUpdateRequest(article, request.RootElement);
            article.Version++;
            await JsonAsync(route, article.Version);
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        var exportButton = Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF", Exact = true });
        await Expect(exportButton).ToBeEnabledAsync();
        await Page.Locator(".preview-settings input[type=range]").Nth(0).EvaluateAsync("input => { input.value = '2'; input.dispatchEvent(new Event('input', { bubbles: true })); }");
        await Page.Locator(".preview-settings input[type=range]").Nth(1).EvaluateAsync("input => { input.value = '1.1'; input.dispatchEvent(new Event('input', { bubbles: true })); }");

        // A changed editor must be explicitly saved before its current snapshot can be exported.
        await Page.GetByLabel("Article Html").FillAsync("Saved before export");
        await Expect(exportButton).ToBeDisabledAsync();
        await exportButton.EvaluateAsync("button => button.click()");
        Assert.Equal(0, exportRequests);
        Assert.Equal(0, saveRequests);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Expect(exportButton).ToBeEnabledAsync();
        Assert.Equal(1, saveRequests);

        await exportButton.ClickAsync();
        await exportStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Preparing PDF…", Exact = true })).ToBeDisabledAsync();
        await exportButton.EvaluateAsync("button => button.click()");
        Assert.Equal(1, exportRequests);

        var downloadTask = Page.WaitForDownloadAsync();
        releaseExport.TrySetResult();
        var download = await downloadTask;
        Assert.Equal("original-slug.pdf", download.SuggestedFilename);
        await Expect(exportButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task EditorPdfExportFailureRestoresTheButtonAndShowsAnAccessibleError()
    {
        var article = new MockArticle();
        var exportId = Guid.NewGuid();
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.RouteAsync($"**/api/admin/articles/en/{article.Slug}/pdf-exports**", route => JsonAsync(route, new
        {
            id = exportId,
            status = "failed",
            errorMessage = "Renderer could not create the PDF.",
            downloadUrl = (string?)null
        }, 202));

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        var exportButton = Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF", Exact = true });
        await exportButton.ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Renderer could not create the PDF.");
        await Expect(exportButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task EditorPdfExport_EditDuringExportResumesAutosaveButStillRequiresManualSaveForAnotherExport()
    {
        Page.SetDefaultTimeout(5_000);
        var article = new MockArticle();
        var exportId = Guid.NewGuid();
        var releaseExport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exportStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var autosaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveRequests = 0;
        await AuthenticateAsync();
        await RouteEditorAsync(article);
        await Page.RouteAsync($"**/api/admin/articles/en/{article.Slug}/pdf-exports**", async route =>
        {
            exportStarted.TrySetResult();
            await releaseExport.Task;
            await JsonAsync(route, new
            {
                id = exportId,
                status = "completed",
                errorMessage = (string?)null,
                downloadUrl = $"/api/admin/pdf-exports/{exportId}/download"
            }, 202);
        });
        await Page.RouteAsync($"**/api/admin/pdf-exports/{exportId}/download", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/pdf",
            BodyBytes = [37, 80, 68, 70, 45, 49, 46, 52]
        }));
        await Page.RouteAsync($"**/api/admin/articles/{article.Id}/localizations/en", async route =>
        {
            if (route.Request.Method != "PUT")
            {
                await route.ContinueAsync();
                return;
            }

            saveRequests++;
            using var request = JsonDocument.Parse(route.Request.PostData!);
            ApplyUpdateRequest(article, request.RootElement);
            article.Version++;
            autosaveStarted.TrySetResult();
            await JsonAsync(route, article.Version);
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());
        var exportButton = Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF", Exact = true });
        await exportButton.ClickAsync();
        await exportStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Page.GetByLabel("Article Html").FillAsync("Autosaved after export");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Preparing PDF…", Exact = true })).ToBeDisabledAsync();

        var downloadTask = Page.WaitForDownloadAsync();
        releaseExport.TrySetResult();
        await downloadTask;
        await autosaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Page.WaitForTimeoutAsync(1_500);

        Assert.Equal(1, saveRequests);
        await Expect(exportButton).ToBeDisabledAsync();
    }

    [Fact]
    public async Task NewArticle_PdfExportIsDisabledUntilTheInitialSave()
    {
        var exportRequests = 0;
        await AuthenticateAsync();
        await RouteNewEditorAsync();
        await Page.RouteAsync("**/api/admin/articles/**/pdf-exports**", async route =>
        {
            exportRequests++;
            await JsonAsync(route, new { });
        });

        await Page.GotoAsync(new Uri(environment.BaseUri, "/admin/articles/new").ToString());
        await Page.GetByLabel("Article title").FillAsync("Save before exporting");
        await Page.GetByLabel("Article Html").FillAsync("A draft needs its initial save.");
        var exportButton = Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF", Exact = true });

        await Expect(exportButton).ToBeDisabledAsync();
        await exportButton.EvaluateAsync("button => button.click()");
        Assert.Equal(0, exportRequests);
    }

    [Fact]
    public async Task ArticleEditor_HidesPdfExportForAnAuthor()
    {
        var article = new MockArticle();
        await AuthenticateAsAuthorAsync(Page);
        await RouteEditorAsync(article);

        await Page.GotoAsync(new Uri(environment.BaseUri, $"/admin/articles/{article.Id}").ToString());

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF", Exact = true })).ToHaveCountAsync(0);
    }

    private Task AuthenticateAsync() => AuthenticateAsync(Page);

    private static async Task AuthenticateAsync(IPage page)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"sub\":\"editor\",\"role\":\"Admin\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await page.AddInitScriptAsync($"sessionStorage.setItem('gaifulinlab.admin.access_token','header.{payload}.signature');");
    }

    private static async Task AuthenticateAsAuthorAsync(IPage page)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"sub\":\"author\",\"role\":\"Author\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await page.AddInitScriptAsync($"sessionStorage.setItem('gaifulinlab.admin.access_token','header.{payload}.signature');");
    }

    private async Task RouteNewEditorAsync()
    {
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview") { await JsonAsync(route, new { html = "<p>Preview</p>" }); return; }
            await JsonAsync(route, new { });
        });
    }

    private static async Task RouteConflictEditorAsync(
        IPage page,
        MockArticle article,
        TaskCompletionSource? successfulSave = null)
    {
        await page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                var html = request.RootElement.GetProperty("html").GetString();
                await JsonAsync(route, new { html = $"<p>{html}</p>" });
                return;
            }
            if (path == $"/api/admin/articles/{article.Id}" && route.Request.Method == "GET") { await JsonAsync(route, Details(article)); return; }
            if (path == $"/api/admin/articles/{article.Id}/localizations/en" && route.Request.Method == "PUT")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                var requestedVersion = request.RootElement.GetProperty("expectedVersion").GetInt64();
                if (requestedVersion != article.Version)
                {
                    await ErrorAsync(route, 409, "article_edit_conflict", "This localization was changed elsewhere.");
                    return;
                }

                ApplyUpdateRequest(article, request.RootElement);
                article.Version++;
                await JsonAsync(route, article.Version);
                successfulSave?.TrySetResult();
                return;
            }
            await JsonAsync(route, new { });
        });
    }

    private static async Task RouteMultiEditorAsync(IPage page, MultiArticle article)
    {
        await page.RouteAsync("**/api/admin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, MultiTaxonomy(article)); return; }
            if (path == "/api/admin/html/preview")
            {
                using var preview = JsonDocument.Parse(route.Request.PostData!);
                await JsonAsync(route, new { html = $"<p>{preview.RootElement.GetProperty("html").GetString()}</p>" });
                return;
            }
            if (path == "/api/admin/articles" && route.Request.Method == "POST")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                var language = request.RootElement.GetProperty("languageCode").GetString()!;
                var localization = new MultiLocalization(
                    Guid.NewGuid(),
                    1,
                    language,
                    request.RootElement.GetProperty("slug").GetString() ?? "",
                    request.RootElement.GetProperty("title").GetString() ?? "",
                    request.RootElement.GetProperty("summary").GetString() ?? "",
                    request.RootElement.GetProperty("html").GetString() ?? "",
                    0);
                article.Localizations[language] = localization;
                await JsonAsync(route, new { articleId = article.Id, localizationId = localization.Id, localizationVersion = localization.Version }, 201);
                return;
            }
            if (path == $"/api/admin/articles/{article.Id}" && route.Request.Method == "GET")
            {
                if (article.ConflictReturned && article.FailConflictLoad)
                {
                    await ErrorAsync(route, 500, "load_failed", "The latest version is unavailable.");
                    return;
                }
                await JsonAsync(route, MultiDetails(article));
                return;
            }
            if (path == $"/api/admin/articles/{article.Id}/taxonomy" && route.Request.Method == "PUT")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                article.TopicIds = request.RootElement.GetProperty("topicIds").EnumerateArray()
                    .Select(item => item.GetGuid()).ToArray();
                article.SeriesAssignments = request.RootElement.GetProperty("series").EnumerateArray()
                    .ToDictionary(
                        item => item.GetProperty("seriesId").GetGuid(),
                        item => item.GetProperty("position").GetInt32());
                article.Tags = request.RootElement.GetProperty("tags").EnumerateArray()
                    .Select(item => item.GetString()?.Trim() ?? "")
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }
            var prefix = $"/api/admin/articles/{article.Id}/localizations/";
            if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith("/publish", StringComparison.Ordinal) && route.Request.Method == "POST")
            {
                var language = path[prefix.Length..^"/publish".Length];
                article.Localizations[language] = article.Localizations[language] with { Status = 1 };
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }
            if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith("/unpublish", StringComparison.Ordinal) && route.Request.Method == "POST")
            {
                var language = path[prefix.Length..^"/unpublish".Length];
                article.Localizations[language] = article.Localizations[language] with { Status = 2 };
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }
            if (path.StartsWith(prefix, StringComparison.Ordinal) && route.Request.Method == "PUT")
            {
                var language = path[prefix.Length..];
                using var request = JsonDocument.Parse(route.Request.PostData!);
                var hasCurrent = article.Localizations.TryGetValue(language, out var current);
                var expectedProperty = request.RootElement.GetProperty("expectedVersion");
                var expectedVersion = expectedProperty.ValueKind == JsonValueKind.Null ? (long?)null : expectedProperty.GetInt64();
                if (article.ForceConflicts || hasCurrent && expectedVersion != current!.Version)
                {
                    article.ConflictReturned = true;
                    await ErrorAsync(route, 409, "article_edit_conflict", "This localization was changed elsewhere.");
                    return;
                }

                var nextVersion = hasCurrent ? current!.Version + 1 : 1;
                article.Localizations[language] = new MultiLocalization(
                    hasCurrent ? current!.Id : Guid.NewGuid(),
                    nextVersion,
                    language,
                    request.RootElement.GetProperty("slug").GetString() ?? "",
                    request.RootElement.GetProperty("title").GetString() ?? "",
                    request.RootElement.GetProperty("summary").GetString() ?? "",
                    request.RootElement.GetProperty("html").GetString() ?? "",
                    hasCurrent ? current!.Status : 0);
                article.ConflictReturned = false;
                await JsonAsync(route, nextVersion);
                return;
            }
            await JsonAsync(route, new { });
        });
    }

    private static async Task RouteMultiPublicAsync(IPage page, MultiArticle article)
    {
        await page.RouteAsync("**/api/public/articles/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 5) { await route.FulfillAsync(new() { Status = 404 }); return; }
            var language = segments[3];
            var slug = segments[4];
            if (!article.Localizations.TryGetValue(language, out var localization)
                || localization.Status != 1
                || !string.Equals(localization.Slug, slug, StringComparison.Ordinal))
            {
                await route.FulfillAsync(new() { Status = 404 });
                return;
            }
            if (segments.Length == 6 && segments[5] == "views")
            {
                await JsonAsync(route, new { viewCount = 1L });
                return;
            }
            var available = article.Localizations.Values
                .Where(item => item.Status == 1)
                .Select(item => new { languageCode = item.Language, url = $"/{item.Language}/articles/{item.Slug}" })
                .ToArray();
            await JsonAsync(route, new
            {
                languageCode = localization.Language,
                slug = localization.Slug,
                title = localization.Title,
                summary = localization.Summary,
                html = $"<p>{localization.Html}</p>",
                publishedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                updatedAt = DateTimeOffset.UtcNow,
                lastEditedAt = DateTimeOffset.UtcNow,
                authorDisplayName = "Translation Author",
                availableLocalizations = available,
                topics = Array.Empty<object>(),
                series = Array.Empty<object>(),
                tags = Array.Empty<string>(),
                viewCount = 0L
            });
        });
    }

    private async Task RouteEditorAsync(MockArticle article, Func<IRoute, int?>? overrideStatus = null)
    {
        await Page.RouteAsync("**/api/admin/**", async route =>
        {
            var status = overrideStatus?.Invoke(route);
            if (status is not null) { await route.FulfillAsync(new() { Status = status.Value }); return; }
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/admin/taxonomy") { await JsonAsync(route, EmptyTaxonomy()); return; }
            if (path == "/api/admin/html/preview") { await JsonAsync(route, new { html = "<p>Preview</p>" }); return; }
            if (path == $"/api/admin/articles/{article.Id}" && route.Request.Method == "GET") { await JsonAsync(route, Details(article)); return; }
            if (path.Contains($"/api/admin/articles/{article.Id}/localizations/en", StringComparison.Ordinal) && route.Request.Method == "PUT")
            {
                using var request = JsonDocument.Parse(route.Request.PostData!);
                ApplyUpdateRequest(article, request.RootElement);
                article.Version++;
                await JsonAsync(route, article.Version);
                return;
            }
            if (path == $"/api/admin/articles/{article.Id}" && route.Request.Method == "DELETE") { await route.FulfillAsync(new() { Status = 204 }); return; }
            await JsonAsync(route, new { });
        });
    }

    private async Task OpenActionsAsync()
    {
        var menu = Page.Locator("details.action-menu");
        if (await menu.GetAttributeAsync("open") is null)
        {
            await Page.GetByLabel("More article actions").ClickAsync();
        }
    }
    private static object EmptyTaxonomy() => new { topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>() };
    private static object ListItem(Guid id, string title, int status) => new { id, createdAt = DateTimeOffset.UtcNow.AddDays(-1), updatedAt = DateTimeOffset.UtcNow, localizations = new[] { new { id = Guid.NewGuid(), languageCode = "en", slug = title.ToLowerInvariant().Replace(' ', '-'), title, status, publishedAt = (DateTimeOffset?)null, updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow } } };
    private static object Details(MockArticle article) => new { id = article.Id, createdAt = DateTimeOffset.UtcNow.AddDays(-1), updatedAt = DateTimeOffset.UtcNow, localizations = new[] { new { id = article.LocalizationId, version = article.Version, languageCode = "en", slug = article.Slug, title = article.Title, summary = article.Summary, html = article.Html, status = article.Status, publishedAt = article.Status == 1 ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-1) : null, updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow } }, topicIds = Array.Empty<Guid>(), series = Array.Empty<object>(), tags = article.Tags };
    private static object PublicDetails(MockArticle article) => new { languageCode = "en", slug = article.Slug, title = article.Title, summary = article.Summary, html = $"<p>{article.Html}</p>", publishedAt = DateTimeOffset.UtcNow.AddMinutes(-1), updatedAt = DateTimeOffset.UtcNow, lastEditedAt = DateTimeOffset.UtcNow, authorDisplayName = "Test Author", availableLocalizations = new[] { new { languageCode = "en", url = $"/en/articles/{article.Slug}" } }, topics = Array.Empty<object>(), series = Array.Empty<object>(), tags = Array.Empty<string>(), viewCount = 0L };
    private static object MultiDetails(MultiArticle article) => new
    {
        id = article.Id,
        createdAt = DateTimeOffset.UtcNow.AddDays(-1),
        updatedAt = DateTimeOffset.UtcNow,
        localizations = article.Localizations.Values.Select(localization => new
        {
            id = localization.Id,
            version = localization.Version,
            languageCode = localization.Language,
            slug = localization.Slug,
            title = localization.Title,
            summary = localization.Summary,
            html = localization.Html,
            status = localization.Status,
            publishedAt = localization.Status == 1 ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1) : null,
            updatedAt = DateTimeOffset.UtcNow,
            lastEditedAt = DateTimeOffset.UtcNow
        }).ToArray(),
        topicIds = article.TopicIds,
        series = article.SeriesAssignments.Select(item => new { seriesId = item.Key, position = item.Value }).ToArray(),
        tags = article.Tags
    };
    private static object MultiTaxonomy(MultiArticle article) => article.Taxonomy is null
        ? EmptyTaxonomy()
        : new
        {
            topics = article.Taxonomy.Topics.Select(item => new
            {
                id = item.Id,
                createdAt = DateTimeOffset.UtcNow.AddDays(-1),
                updatedAt = DateTimeOffset.UtcNow,
                localizations = new[] { new { id = Guid.NewGuid(), languageCode = "en", name = item.Name, slug = item.Name.ToLowerInvariant().Replace(' ', '-'), description = "" } }
            }).ToArray(),
            series = article.Taxonomy.Series.Select(item => new
            {
                id = item.Id,
                createdAt = DateTimeOffset.UtcNow.AddDays(-1),
                updatedAt = DateTimeOffset.UtcNow,
                localizations = new[] { new { id = Guid.NewGuid(), languageCode = "en", title = item.Name, slug = item.Name.ToLowerInvariant().Replace(' ', '-'), description = "" } },
                articles = Array.Empty<object>()
            }).ToArray(),
            tags = Array.Empty<object>()
        };
    private static Task JsonAsync(IRoute route, object body, int status = 200) => route.FulfillAsync(new() { Status = status, ContentType = "application/json", Body = JsonSerializer.Serialize(body, body.GetType()) });
    private static Task ErrorAsync(IRoute route, int status, string code, string message) => JsonAsync(route, new { code, message }, status);
    private static void ApplyCreateRequest(MockArticle article, string postData)
    {
        using var request = JsonDocument.Parse(postData);
        ApplyUpdateRequest(article, request.RootElement);
    }
    private static void ApplyUpdateRequest(MockArticle article, JsonElement request)
    {
        article.Title = request.GetProperty("title").GetString() ?? "";
        article.Summary = request.GetProperty("summary").GetString() ?? "";
        article.Html = request.GetProperty("html").GetString() ?? "";
        article.Slug = request.GetProperty("slug").GetString() ?? "";
    }
    private sealed class MockArticle { public Guid Id { get; } = Guid.NewGuid(); public Guid LocalizationId { get; } = Guid.NewGuid(); public long Version { get; set; } = 1; public string Title { get; set; } = "Original title"; public string Summary { get; set; } = "Original summary"; public string Html { get; set; } = "Original body"; public string Slug { get; set; } = "original-slug"; public string[] Tags { get; set; } = []; public int Status { get; set; } }
    private sealed class MultiArticle
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Dictionary<string, MultiLocalization> Localizations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ConflictReturned { get; set; }
        public bool FailConflictLoad { get; set; }
        public bool ForceConflicts { get; set; }
        public TestTaxonomy? Taxonomy { get; set; }
        public Guid[] TopicIds { get; set; } = [];
        public Dictionary<Guid, int> SeriesAssignments { get; set; } = [];
        public string[] Tags { get; set; } = [];

        public static MultiArticle WithEnglishAndRussian()
        {
            var article = new MultiArticle();
            article.Localizations["en"] = new(Guid.NewGuid(), 1, "en", "english-original", "English original", "English summary", "English Html", 0);
            article.Localizations["ru"] = new(Guid.NewGuid(), 5, "ru", "russkiy-original", "Русский без изменений", "Русское описание", "Русский текст без изменений", 0);
            return article;
        }

        public static MultiArticle WithEnglishDraft()
        {
            var article = new MultiArticle();
            article.Localizations["en"] = new(Guid.NewGuid(), 1, "en", "english-original", "English original", "English summary", "English body", 0);
            return article;
        }
    }
    private sealed record MultiLocalization(Guid Id, long Version, string Language, string Slug, string Title, string Summary, string Html, int Status);
    private sealed record TaxonomyItem(Guid Id, string Name);
    private sealed record TestTaxonomy(TaxonomyItem[] Topics, TaxonomyItem[] Series)
    {
        public static TestTaxonomy Create() => new(
            [new(Guid.NewGuid(), "Engineering"), new(Guid.NewGuid(), "Signal processing")],
            [new(Guid.NewGuid(), "Series One"), new(Guid.NewGuid(), "Series Two")]);
    }
}
