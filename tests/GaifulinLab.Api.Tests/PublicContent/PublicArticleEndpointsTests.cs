using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Api.Tests.Authentication;
using GaifulinLab.Contracts.Articles;

namespace GaifulinLab.Api.Tests.PublicContent;

public sealed class PublicArticleEndpointsTests
{
    [Fact]
    public async Task Articles_WhenDatabaseIsEmpty_ReturnEmptyCollection()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateClient();

        var articles = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en");

        Assert.Empty(articles!);
    }

    [Fact]
    public async Task ArticleDetails_ReturnOnlyPublishedIndependentLocalizations()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        var article = await client.GetFromJsonAsync<PublicArticleDetailsDto>(
            "/api/public/articles/en/understanding-fft");

        Assert.NotNull(article);
        Assert.Equal("en", article.LanguageCode);
        Assert.Equal("Understanding FFT", article.Title);
        Assert.Equal("Test Author", article.AuthorDisplayName);
        Assert.Contains("<strong>safe</strong>", article.Html);
        Assert.DoesNotContain("<script", article.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, article.ViewCount);
        Assert.Equal(article.PublishedAt, article.LastEditedAt);
        Assert.Equal(
            [
                new AvailableLocalizationDto("en", "/en/articles/understanding-fft"),
                new AvailableLocalizationDto("ru", "/ru/articles/kak-rabotaet-fft")
            ],
            article.AvailableLocalizations);

        var noFallback = await client.GetAsync("/api/public/articles/ru/understanding-fft");
        var russian = await client.GetAsync("/api/public/articles/ru/kak-rabotaet-fft");
        var draft = await client.GetAsync("/api/public/articles/en/future-draft");
        var deleted = await client.GetAsync("/api/public/articles/en/deleted-published");

        Assert.Equal(HttpStatusCode.NotFound, noFallback.StatusCode);
        Assert.Equal(HttpStatusCode.OK, russian.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Fact]
    public async Task ArticleView_RecordsOnlyOneViewPerArticleAndVisitor()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        var first = await PublicContentTestData.PostArticleViewAsync(client, "en", "understanding-fft", "198.51.100.10");
        var firstCount = await first.Content.ReadFromJsonAsync<ArticleViewCountDto>();
        var repeated = await PublicContentTestData.PostArticleViewAsync(client, "en", "understanding-fft", "198.51.100.10");
        var repeatedCount = await repeated.Content.ReadFromJsonAsync<ArticleViewCountDto>();
        var localized = await PublicContentTestData.PostArticleViewAsync(client, "ru", "kak-rabotaet-fft", "198.51.100.10");
        var localizedCount = await localized.Content.ReadFromJsonAsync<ArticleViewCountDto>();
        var differentVisitor = await PublicContentTestData.PostArticleViewAsync(client, "en", "understanding-fft", "198.51.100.11");
        var differentVisitorCount = await differentVisitor.Content.ReadFromJsonAsync<ArticleViewCountDto>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, localized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, differentVisitor.StatusCode);
        Assert.Equal(1, firstCount?.ViewCount);
        Assert.Equal(1, repeatedCount?.ViewCount);
        Assert.Equal(1, localizedCount?.ViewCount);
        Assert.Equal(2, differentVisitorCount?.ViewCount);

        var details = await client.GetFromJsonAsync<PublicArticleDetailsDto>(
            "/api/public/articles/en/understanding-fft");
        Assert.Equal(2, details?.ViewCount);
    }

    [Fact]
    public async Task ArticleDetails_ExposeDisplayNameButNotIdentityFields()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        var payload = await client.GetStringAsync("/api/public/articles/en/understanding-fft");

        Assert.Contains("Test Author", payload);
        Assert.DoesNotContain("test-owner", payload);
        Assert.DoesNotContain("private-owner@example.com", payload);
    }

    [Fact]
    public async Task ArticleView_ForDraftLocalization_ReturnsNotFound()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/public/articles/en/future-draft/views", content: null);
        var deletedResponse = await client.PostAsync("/api/public/articles/en/deleted-published/views", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
    }

    [Fact]
    public async Task ArticleList_IncludesOnlyPublishedArticlesAndSupportsCombinedFilters()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        var filtered = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&topic=signal-processing&series=fourier-notes&tag=.NET");

        var article = Assert.Single(filtered!);
        Assert.Equal("understanding-fft", article.Slug);
        Assert.Equal("Test Author", article.AuthorDisplayName);
        Assert.Equal("Signal processing", Assert.Single(article.Topics).DisplayName);
        Assert.Equal("Fourier notes", Assert.Single(article.Series).DisplayName);
        Assert.Equal([".NET"], article.Tags);

        var allEnglish = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en");
        Assert.Single(allEnglish!);
    }

    [Fact]
    public async Task ArticleList_PaginatesInPublishedOrderWithoutDuplicatingArticles()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        await PublicContentTestData.SeedPublishedEnglishArticlesAsync(factory.Services, 22);
        using var client = factory.CreateClient();

        var firstPage = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&page=1&pageSize=10");
        var secondPage = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&page=2&pageSize=10");
        var thirdPage = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&page=3&pageSize=10");

        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);
        Assert.NotNull(thirdPage);
        Assert.Equal(10, firstPage.Count);
        Assert.Equal(10, secondPage.Count);
        Assert.Equal(3, thirdPage.Count);

        var articles = firstPage.Concat(secondPage).Concat(thirdPage).ToArray();
        Assert.Equal(23, articles.Select(article => article.Slug).Distinct().Count());
        Assert.Equal(
            articles.OrderByDescending(article => article.PublishedAt).Select(article => article.Slug),
            articles.Select(article => article.Slug));
    }
}
