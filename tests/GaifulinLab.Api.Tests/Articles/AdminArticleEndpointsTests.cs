using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Series;
using GaifulinLab.Domain.Common;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Persistence;
using GaifulinLab.Api.Tests.Authentication;
using GaifulinLab.Infrastructure.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace GaifulinLab.Api.Tests.Articles;

public sealed class AdminArticleEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    private static int _nextTestClientIp;

    [Fact]
    public async Task Articles_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/articles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ArticleLifecycle_PreservesIndependentLocalizations()
    {
        using var client = await CreateAuthenticatedClient();
        var slug = $"article-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest("en", "English title", "Summary", "# Content", slug));

        Assert.True(
            createResponse.StatusCode == HttpStatusCode.Created,
            $"Expected 201, received {(int)createResponse.StatusCode}: {await createResponse.Content.ReadAsStringAsync()}");
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);
        Assert.Equal($"/api/admin/articles/{created.ArticleId}", createResponse.Headers.Location?.ToString());

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/ru",
            new UpdateArticleLocalizationRequest(
                "Русский заголовок",
                null,
                "# Содержимое",
                $"statya-{Guid.NewGuid():N}"));
        Assert.True(
            updateResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200, received {(int)updateResponse.StatusCode}: {await updateResponse.Content.ReadAsStringAsync()}");

        var publishResponse = await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish",
            null);
        Assert.Equal(HttpStatusCode.NoContent, publishResponse.StatusCode);

        var unpublishResponse = await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/unpublish",
            null);
        Assert.Equal(HttpStatusCode.NoContent, unpublishResponse.StatusCode);

        var unpublishedArticle = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        Assert.Equal(
            PublicationStatusDto.Unpublished,
            unpublishedArticle!.Localizations.Single(localization => localization.LanguageCode == "en").Status);

        var republishResponse = await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish",
            null);
        Assert.Equal(HttpStatusCode.NoContent, republishResponse.StatusCode);

        var article = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        Assert.NotNull(article);
        Assert.Collection(
            article.Localizations,
            english =>
            {
                Assert.Equal("en", english.LanguageCode);
                Assert.Equal(PublicationStatusDto.Published, english.Status);
            },
            russian =>
            {
                Assert.Equal("ru", russian.LanguageCode);
                Assert.Equal(PublicationStatusDto.Draft, russian.Status);
            });

        var list = await client.GetFromJsonAsync<IReadOnlyList<AdminArticleListItemDto>>(
            "/api/admin/articles");
        Assert.Contains(list!, item => item.Id == created.ArticleId);

        var deleteResponse = await client.DeleteAsync($"/api/admin/articles/{created.ArticleId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var missingResponse = await client.GetAsync($"/api/admin/articles/{created.ArticleId}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/public/articles/en/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(
            $"/api/public/articles/en/{slug}/views", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(
            $"/api/admin/articles/en/{slug}/pdf-exports", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest("Changed", null, "# Changed", "changed"))).StatusCode);

        var listAfterDelete = await client.GetFromJsonAsync<IReadOnlyList<AdminArticleListItemDto>>(
            "/api/admin/articles");
        Assert.DoesNotContain(listAfterDelete!, item => item.Id == created.ArticleId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deleted = await dbContext.Articles.FindAsync(created.ArticleId);
        Assert.NotNull(deleted);
        Assert.NotNull(deleted!.DeletedAt);
        Assert.True(dbContext.ArticleLocalizations.Any(
            localization => localization.ArticleId == created.ArticleId));
    }

    [Fact]
    public async Task TranslationPublication_IsIndependentForEachLanguageAndKeepsBothVersionsIntact()
    {
        using var client = await CreateAuthenticatedClient();
        var enSlug = $"translation-en-{Guid.NewGuid():N}";
        var ruSlug = $"translation-ru-{Guid.NewGuid():N}";
        var created = await (await client.PostAsJsonAsync(
            "/api/admin/articles", new CreateArticleRequest("en", "English", "EN summary", "EN body", enSlug)))
            .Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        var russian = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created!.ArticleId}/localizations/ru",
            new UpdateArticleLocalizationRequest("Русский", "RU summary", "RU body", ruSlug));
        Assert.Equal(HttpStatusCode.OK, russian.StatusCode);
        await client.PostAsync($"/api/admin/articles/{created.ArticleId}/localizations/en/publish", null);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/public/articles/en/{enSlug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/public/articles/ru/{ruSlug}")).StatusCode);

        await client.PostAsync($"/api/admin/articles/{created.ArticleId}/localizations/ru/publish", null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/public/articles/ru/{ruSlug}")).StatusCode);
        await client.PostAsync($"/api/admin/articles/{created.ArticleId}/localizations/en/unpublish", null);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/public/articles/en/{enSlug}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/public/articles/ru/{ruSlug}")).StatusCode);

        var details = await client.GetFromJsonAsync<AdminArticleDetailsDto>($"/api/admin/articles/{created.ArticleId}");
        Assert.Equal("EN body", details!.Localizations.Single(item => item.LanguageCode == "en").Markdown);
        Assert.Equal("RU body", details.Localizations.Single(item => item.LanguageCode == "ru").Markdown);
    }

    [Fact]
    public async Task PublishingAfterAnUpdate_ExposesTheLatestLocalizationRatherThanTheOriginalDraft()
    {
        using var client = await CreateAuthenticatedClient();
        var slug = $"latest-before-publish-{Guid.NewGuid():N}";
        var created = await (await client.PostAsJsonAsync(
            "/api/admin/articles", new CreateArticleRequest("en", "Initial title", null, "Initial body", slug)))
            .Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);
        var update = await client.PutAsJsonAsync($"/api/admin/articles/{created!.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest("Latest title", "Latest summary", "Latest body", slug, created.LocalizationVersion));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish", null)).StatusCode);
        var publicArticle = await client.GetFromJsonAsync<PublicArticleDetailsDto>($"/api/public/articles/en/{slug}");
        Assert.Equal("Latest title", publicArticle!.Title);
        Assert.Contains("Latest body", publicArticle.Html);
    }

    [Theory]
    [InlineData("", "valid-slug")]
    [InlineData("Valid title", "")]
    public async Task Publish_RejectsDraftMissingRequiredFields(string title, string slug)
    {
        using var client = await CreateAuthenticatedClient();
        var created = await (await client.PostAsJsonAsync("/api/admin/articles", new CreateArticleRequest("en", title, null, "Body", slug)))
            .Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);
        var response = await client.PostAsync($"/api/admin/articles/{created!.ArticleId}/localizations/en/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unpublish_RejectsDraftAndMakesTheLocalizationPrivateUntilRepublished()
    {
        using var client = await CreateAuthenticatedClient();
        var slug = $"lifecycle-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest("en", "Title", null, "# Content", slug));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(
            $"/api/admin/articles/{created!.ArticleId}/localizations/en/unpublish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/public/articles/en/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/unpublish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/public/articles/en/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish", null)).StatusCode);
    }

    [Fact]
    public async Task RetryAfterARejectedLocalizationUpdate_UpdatesTheOriginalArticleWithoutCreatingADuplicate()
    {
        using var client = await CreateAuthenticatedClient();
        var slug = $"retry-{Guid.NewGuid():N}";
        var created = await (await client.PostAsJsonAsync(
            "/api/admin/articles", new CreateArticleRequest("en", "Initial", null, "Initial body", slug)))
            .Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        var invalid = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created!.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest("Updated", new string('s', ContentLimits.ArticleSummary + 1), "Updated body", slug, created.LocalizationVersion));
        await AssertInvalidFieldAsync(invalid, "Summary");

        var retry = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest("Updated", "Updated summary", "Updated body", slug, created.LocalizationVersion));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var list = await client.GetFromJsonAsync<IReadOnlyList<AdminArticleListItemDto>>("/api/admin/articles");
        Assert.Single(list!, item => item.Id == created.ArticleId);
        var saved = await client.GetFromJsonAsync<AdminArticleDetailsDto>($"/api/admin/articles/{created.ArticleId}");
        Assert.Equal("Updated", Assert.Single(saved!.Localizations).Title);
        Assert.Equal("Updated body", Assert.Single(saved.Localizations).Markdown);
    }

    [Fact]
    public async Task UpdateLocalization_RejectsAStaleVersionWithoutOverwritingNewerContent()
    {
        using var client = await CreateAuthenticatedClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest(
                "en",
                "Original title",
                null,
                "# Original",
                $"concurrency-{Guid.NewGuid():N}"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        var firstCopy = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created!.ArticleId}");
        var secondCopy = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        var firstLocalization = Assert.Single(firstCopy!.Localizations);
        var secondLocalization = Assert.Single(secondCopy!.Localizations);
        Assert.Equal(firstLocalization.Version, secondLocalization.Version);

        var firstUpdate = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest(
                "First editor",
                null,
                "# Saved by first editor",
                firstLocalization.Slug,
                firstLocalization.Version));
        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);
        Assert.Equal(firstLocalization.Version + 1, await firstUpdate.Content.ReadFromJsonAsync<long>());

        var staleUpdate = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest(
                "Second editor",
                null,
                "# Stale overwrite",
                secondLocalization.Slug,
                secondLocalization.Version));
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var error = await staleUpdate.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("article_edit_conflict", error?.Code);

        var persisted = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        var localization = Assert.Single(persisted!.Localizations);
        Assert.Equal("First editor", localization.Title);
        Assert.Equal("# Saved by first editor", localization.Markdown);
        Assert.Equal(firstLocalization.Version + 1, localization.Version);
    }

    [Fact]
    public async Task CreateArticle_WithDuplicateLanguageSlug_ReturnsConflict()
    {
        using var client = await CreateAuthenticatedClient();
        var slug = $"duplicate-{Guid.NewGuid():N}";
        var request = new CreateArticleRequest("en", "Title", null, "Content", slug);

        var firstResponse = await client.PostAsJsonAsync("/api/admin/articles", request);
        var secondResponse = await client.PostAsJsonAsync("/api/admin/articles", request);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var error = await secondResponse.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("conflict", error?.Code);
    }

    [Fact]
    public async Task ContentLongerThanPersistedLimits_ReturnsFieldValidationErrorsBeforeSaving()
    {
        using var client = await CreateAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest(
                "en",
                new string('t', ContentLimits.ArticleTitle + 1),
                null,
                "Content",
                "too-long-title"));
        await AssertInvalidFieldAsync(createResponse, "Title");

        var validResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest("en", "Original title", "Summary", "Content", $"limits-{Guid.NewGuid():N}"));
        var created = await validResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        var summaryResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created!.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest(
                "Original title",
                new string('s', ContentLimits.ArticleSummary + 1),
                "Content",
                "valid-slug",
                created.LocalizationVersion));
        await AssertInvalidFieldAsync(summaryResponse, "Summary");

        var slugResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest(
                "Original title",
                "Summary",
                "Content",
                new string('a', ContentLimits.ArticleSlug + 1),
                created.LocalizationVersion));
        await AssertInvalidFieldAsync(slugResponse, "Slug");

        var tagsResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [], [new string('t', ContentLimits.TagName + 1)]));
        await AssertInvalidFieldAsync(tagsResponse, "Tags");

        var persisted = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        var localization = Assert.Single(persisted!.Localizations);
        Assert.Equal("Original title", localization.Title);
        Assert.Equal("Summary", localization.Summary);
    }

    [Fact]
    public async Task Articles_AreVisibleAndEditableOnlyByTheirOwner()
    {
        var ownerLogin = $"owner-{Guid.NewGuid():N}";
        var otherLogin = $"other-{Guid.NewGuid():N}";
        const string password = "Strong-password-1!";
        using var ownerClient = await CreateUserClient(factory, ownerLogin, password);
        using var otherClient = await CreateUserClient(factory, otherLogin, password);

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest("en", "Owner article", null, "# Owner", $"owner-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        // A second account cannot discover or change a draft even when it knows its identifier.
        var otherList = await otherClient.GetFromJsonAsync<IReadOnlyList<AdminArticleListItemDto>>(
            "/api/admin/articles");
        Assert.DoesNotContain(otherList!, article => article.Id == created.ArticleId);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync(
            $"/api/admin/articles/{created.ArticleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest("Changed", null, "# Changed", "changed"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish",
            null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/unpublish",
            null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [], []))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync(
            $"/api/admin/articles/{created.ArticleId}")).StatusCode);

        var ownerArticle = await ownerClient.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        Assert.Equal("Owner article", ownerArticle!.Localizations.Single().Title);

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await userManager.FindByNameAsync(ownerLogin);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(owner!.Id, (await dbContext.Articles.FindAsync(created.ArticleId))!.OwnerUserId);
    }

    [Fact]
    public async Task Taxonomy_WhenDatabaseIsEmpty_ReturnsEmptyCollections()
    {
        using var client = await CreateAuthenticatedClient();

        var taxonomy = await client.GetFromJsonAsync<AdminTaxonomyDto>("/api/admin/taxonomy");

        Assert.NotNull(taxonomy);
        Assert.Empty(taxonomy.Topics);
        Assert.Empty(taxonomy.Series);
        Assert.Empty(taxonomy.Tags);
    }

    [Fact]
    public async Task UpdateTaxonomy_ReplacesAssignmentsAndCreatesMissingTags()
    {
        await using var isolatedFactory = new AuthWebApplicationFactory();
        Guid topicId;
        Guid seriesId;

        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var topic = Topic.Create("en", "Engineering", "engineering", null, now);
            var series = Series.Create("en", "Fourier transforms", "fourier-transforms", null, now);
            dbContext.AddRange(topic, series);
            await dbContext.SaveChangesAsync();
            topicId = topic.Id;
            seriesId = series.Id;
        }

        using var client = await CreateAuthenticatedClient(isolatedFactory);
        var articleSlug = $"fft-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest(
                "en",
                "FFT",
                null,
                "# FFT",
                articleSlug));
        var article = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{article!.ArticleId}/taxonomy",
            new UpdateArticleTaxonomyRequest(
                [topicId],
                [new SeriesAssignmentRequest(seriesId, 1)],
                ["dotnet", "Backend", "DOTNET"]));

        Assert.True(
            assignResponse.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204, received {(int)assignResponse.StatusCode}: {await assignResponse.Content.ReadAsStringAsync()}");
        var details = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{article.ArticleId}");
        Assert.Equal([topicId], details!.TopicIds);
        Assert.Equal(new SeriesAssignmentDto(seriesId, 1), Assert.Single(details.Series));
        Assert.Equal(["Backend", "dotnet"], details.Tags);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{article.ArticleId}/localizations/en/publish",
            null)).StatusCode);
        var publicDetails = await client.GetFromJsonAsync<PublicArticleDetailsDto>(
            $"/api/public/articles/en/{articleSlug}");
        Assert.Equal("Engineering", Assert.Single(publicDetails!.Topics).DisplayName);
        Assert.Equal("Fourier transforms", Assert.Single(publicDetails.Series).DisplayName);
        Assert.Equal(["Backend", "dotnet"], publicDetails.Tags);
        Assert.Single((await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&topic=engineering"))!);
        Assert.Single((await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&series=fourier-transforms"))!);
        Assert.Single((await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&tag=dotnet"))!);

        var clearResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{article.ArticleId}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [], []));
        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);

        details = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{article.ArticleId}");
        Assert.Empty(details!.TopicIds);
        Assert.Empty(details.Series);
        Assert.Empty(details.Tags);
        publicDetails = await client.GetFromJsonAsync<PublicArticleDetailsDto>(
            $"/api/public/articles/en/{articleSlug}");
        Assert.Empty(publicDetails!.Topics);
        Assert.Empty(publicDetails.Series);
        Assert.Empty(publicDetails.Tags);
        Assert.Empty((await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&topic=engineering"))!);
        Assert.Empty((await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&series=fourier-transforms"))!);
        Assert.Empty((await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&tag=dotnet"))!);
    }

    [Fact]
    public async Task DeleteArticle_ReleasesItsSeriesPositionWithoutChangingTheRemainingOrder()
    {
        await using var isolatedFactory = new AuthWebApplicationFactory();
        Guid seriesId;

        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var series = Series.Create(
                "en",
                "Deletion test series",
                $"deletion-test-series-{Guid.NewGuid():N}",
                null,
                DateTimeOffset.UtcNow);
            dbContext.Series.Add(series);
            await dbContext.SaveChangesAsync();
            seriesId = series.Id;
        }

        using var client = await CreateAuthenticatedClient(isolatedFactory);
        var first = await CreateArticle(client, "First");
        var second = await CreateArticle(client, "Second");
        var third = await CreateArticle(client, "Third");
        var fourth = await CreateArticle(client, "Fourth");
        var originalArticles = new[] { first, second, third, fourth };

        foreach (var (articleId, position) in originalArticles.Select(
                     (articleId, index) => (articleId, position: index + 1)))
        {
            var response = await client.PutAsJsonAsync(
                $"/api/admin/articles/{articleId}/taxonomy",
                new UpdateArticleTaxonomyRequest(
                    [],
                    [new SeriesAssignmentRequest(seriesId, position)],
                    []));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/admin/articles/{second}")).StatusCode);

        var replacement = await CreateArticle(client, "Replacement");
        var assignReplacement = await client.PutAsJsonAsync(
            $"/api/admin/articles/{replacement}/taxonomy",
            new UpdateArticleTaxonomyRequest(
                [],
                [new SeriesAssignmentRequest(seriesId, 2)],
                []));
        Assert.Equal(HttpStatusCode.NoContent, assignReplacement.StatusCode);

        await using var verificationScope = isolatedFactory.Services.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(verificationDbContext.ArticleSeries.Where(link => link.ArticleId == second));
        var orderedArticleIds = verificationDbContext.ArticleSeries
            .Where(link => link.SeriesId == seriesId)
            .OrderBy(link => link.Position)
            .Select(link => link.ArticleId)
            .ToArray();
        Assert.Equal([first, replacement, third, fourth], orderedArticleIds);
    }

    [Fact]
    public async Task ArticleCanUseDifferentPositionsInTwoSeries_AndChangingOneKeepsBothPublicOrdersCorrect()
    {
        await using var isolatedFactory = new AuthWebApplicationFactory();
        var suffix = Guid.NewGuid().ToString("N");
        var firstSeriesSlug = $"first-series-{suffix}";
        var secondSeriesSlug = $"second-series-{suffix}";
        Guid firstSeriesId;
        Guid secondSeriesId;
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var firstSeries = Series.Create("en", "First series", firstSeriesSlug, null, now);
            var secondSeries = Series.Create("en", "Second series", secondSeriesSlug, null, now);
            db.Series.AddRange(firstSeries, secondSeries);
            await db.SaveChangesAsync();
            firstSeriesId = firstSeries.Id;
            secondSeriesId = secondSeries.Id;
        }

        using var client = await CreateAuthenticatedClient(isolatedFactory);
        var firstAnchor = await CreatePublishedArticle(client, "First anchor", $"first-anchor-{suffix}");
        var target = await CreatePublishedArticle(client, "Target article", $"target-{suffix}");
        var secondAnchor = await CreatePublishedArticle(client, "Second anchor", $"second-anchor-{suffix}");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/admin/articles/{firstAnchor}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [new SeriesAssignmentRequest(firstSeriesId, 1)], []))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/admin/articles/{secondAnchor}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [new SeriesAssignmentRequest(secondSeriesId, 2)], []))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/admin/articles/{target}/taxonomy",
            new UpdateArticleTaxonomyRequest(
                [],
                [new SeriesAssignmentRequest(firstSeriesId, 2), new SeriesAssignmentRequest(secondSeriesId, 1)],
                []))).StatusCode);

        var changeOnePosition = await client.PutAsJsonAsync(
            $"/api/admin/articles/{target}/taxonomy",
            new UpdateArticleTaxonomyRequest(
                [],
                [new SeriesAssignmentRequest(firstSeriesId, 3), new SeriesAssignmentRequest(secondSeriesId, 1)],
                []));
        Assert.Equal(HttpStatusCode.NoContent, changeOnePosition.StatusCode);

        var details = await client.GetFromJsonAsync<AdminArticleDetailsDto>($"/api/admin/articles/{target}");
        Assert.Contains(new SeriesAssignmentDto(firstSeriesId, 3), details!.Series);
        Assert.Contains(new SeriesAssignmentDto(secondSeriesId, 1), details.Series);
        var firstPublic = await client.GetFromJsonAsync<PublicSeriesDetailsDto>($"/api/public/series/en/{firstSeriesSlug}");
        var secondPublic = await client.GetFromJsonAsync<PublicSeriesDetailsDto>($"/api/public/series/en/{secondSeriesSlug}");
        Assert.Equal([("First anchor", 1), ("Target article", 3)], firstPublic!.Articles.Select(item => (item.Title, item.Position)).ToArray());
        Assert.Equal([("Target article", 1), ("Second anchor", 2)], secondPublic!.Articles.Select(item => (item.Title, item.Position)).ToArray());
    }

    [Fact]
    public async Task InvalidOrOccupiedSeriesPosition_IsRejectedWithoutChangingTheExistingOrder()
    {
        await using var isolatedFactory = new AuthWebApplicationFactory();
        var suffix = Guid.NewGuid().ToString("N");
        var seriesSlug = $"position-validation-{suffix}";
        Guid seriesId;
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var series = Series.Create("en", "Position validation", seriesSlug, null, DateTimeOffset.UtcNow);
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        using var client = await CreateAuthenticatedClient(isolatedFactory);
        var first = await CreatePublishedArticle(client, "Occupied first", $"occupied-first-{suffix}");
        var target = await CreatePublishedArticle(client, "Stable second", $"stable-second-{suffix}");
        await client.PutAsJsonAsync($"/api/admin/articles/{first}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [new SeriesAssignmentRequest(seriesId, 1)], []));
        await client.PutAsJsonAsync($"/api/admin/articles/{target}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [new SeriesAssignmentRequest(seriesId, 2)], []));

        var occupied = await client.PutAsJsonAsync($"/api/admin/articles/{target}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [new SeriesAssignmentRequest(seriesId, 1)], []));
        Assert.Equal(HttpStatusCode.BadRequest, occupied.StatusCode);
        Assert.Contains("already occupied", (await occupied.Content.ReadFromJsonAsync<ApiErrorResponse>())!.Message);
        foreach (var invalidPosition in new[] { 0, -1 })
        {
            var invalid = await client.PutAsJsonAsync($"/api/admin/articles/{target}/taxonomy",
                new UpdateArticleTaxonomyRequest([], [new SeriesAssignmentRequest(seriesId, invalidPosition)], []));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("greater than zero", (await invalid.Content.ReadFromJsonAsync<ApiErrorResponse>())!.Message);
        }

        var details = await client.GetFromJsonAsync<AdminArticleDetailsDto>($"/api/admin/articles/{target}");
        Assert.Equal(new SeriesAssignmentDto(seriesId, 2), Assert.Single(details!.Series));
        var publicSeries = await client.GetFromJsonAsync<PublicSeriesDetailsDto>($"/api/public/series/en/{seriesSlug}");
        Assert.Equal([("Occupied first", 1), ("Stable second", 2)], publicSeries!.Articles.Select(item => (item.Title, item.Position)).ToArray());
    }

    private static async Task<Guid> CreateArticle(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest(
                "en",
                title,
                null,
                "# Content",
                $"{title.ToLowerInvariant()}-{Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateArticleResponse>())!.ArticleId;
    }

    private static async Task<Guid> CreatePublishedArticle(HttpClient client, string title, string slug)
    {
        var response = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest("en", title, null, $"# {title}", slug));
        response.EnsureSuccessStatusCode();
        var articleId = (await response.Content.ReadFromJsonAsync<CreateArticleResponse>())!.ArticleId;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{articleId}/localizations/en/publish",
            null)).StatusCode);
        return articleId;
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        return await CreateAuthenticatedClient(factory);
    }

    private static async Task<HttpClient> CreateAuthenticatedClient(
        AuthWebApplicationFactory applicationFactory)
    {
        var client = applicationFactory.CreateClient();
        AssignUniqueClientIp(client);
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthWebApplicationFactory.AdminLogin, AuthWebApplicationFactory.AdminPassword));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static async Task<HttpClient> CreateUserClient(
        AuthWebApplicationFactory applicationFactory,
        string login,
        string password)
    {
        await using (var scope = applicationFactory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await userManager.CreateAsync(
                new ApplicationUser { UserName = login, DisplayName = "Second User" },
                password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        var client = applicationFactory.CreateClient();
        AssignUniqueClientIp(client);
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, password));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private static void AssignUniqueClientIp(HttpClient client)
    {
        var address = (uint)Interlocked.Increment(ref _nextTestClientIp);
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            $"10.{address >> 16 & 255}.{address >> 8 & 255}.{address & 255}");
    }

    private static async Task AssertInvalidFieldAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_request", error?.Code);
        Assert.NotNull(error?.Errors);
        Assert.True(error.Errors!.ContainsKey(field), $"Expected validation error for '{field}'.");
    }
}
