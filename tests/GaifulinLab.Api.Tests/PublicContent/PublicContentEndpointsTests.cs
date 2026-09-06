using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Persistence;
using GaifulinLab.Api.Tests.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Api.Tests.PublicContent;

public sealed class PublicContentEndpointsTests
{
    [Fact]
    public async Task PublicCollections_WhenDatabaseIsEmpty_ReturnEmptyCollections()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateClient();

        var articles = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en");
        var topics = await client.GetFromJsonAsync<IReadOnlyList<PublicTopicDto>>(
            "/api/public/topics/en");
        var series = await client.GetFromJsonAsync<IReadOnlyList<PublicSeriesListItemDto>>(
            "/api/public/series/en");
        var tags = await client.GetFromJsonAsync<IReadOnlyList<PublicTagDto>>(
            "/api/public/tags/en");

        Assert.Empty(articles!);
        Assert.Empty(topics!);
        Assert.Empty(series!);
        Assert.Empty(tags!);
    }

    [Fact]
    public async Task ArticleDetails_ReturnOnlyPublishedIndependentLocalizations()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedContent(factory.Services);
        using var client = factory.CreateClient();

        var article = await client.GetFromJsonAsync<PublicArticleDetailsDto>(
            "/api/public/articles/en/understanding-fft");

        Assert.NotNull(article);
        Assert.Equal("en", article.LanguageCode);
        Assert.Equal("Understanding FFT", article.Title);
        Assert.Contains("<strong>safe</strong>", article.Html);
        Assert.DoesNotContain("<script", article.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, article.ViewCount);
        Assert.Equal(
            [
                new AvailableLocalizationDto("en", "/en/articles/understanding-fft"),
                new AvailableLocalizationDto("ru", "/ru/articles/kak-rabotaet-fft")
            ],
            article.AvailableLocalizations);

        var noFallback = await client.GetAsync("/api/public/articles/ru/understanding-fft");
        var russian = await client.GetAsync("/api/public/articles/ru/kak-rabotaet-fft");
        var draft = await client.GetAsync("/api/public/articles/en/future-draft");

        Assert.Equal(HttpStatusCode.NotFound, noFallback.StatusCode);
        Assert.Equal(HttpStatusCode.OK, russian.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
    }

    [Fact]
    public async Task ArticleView_RecordsOnlyOneViewPerArticleAndVisitor()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedContent(factory.Services);
        using var client = factory.CreateClient();

        var first = await PostArticleViewAsync(client, "en", "understanding-fft", "198.51.100.10");
        var firstCount = await first.Content.ReadFromJsonAsync<ArticleViewCountDto>();
        var repeated = await PostArticleViewAsync(client, "en", "understanding-fft", "198.51.100.10");
        var repeatedCount = await repeated.Content.ReadFromJsonAsync<ArticleViewCountDto>();
        var localized = await PostArticleViewAsync(client, "ru", "kak-rabotaet-fft", "198.51.100.10");
        var localizedCount = await localized.Content.ReadFromJsonAsync<ArticleViewCountDto>();
        var differentVisitor = await PostArticleViewAsync(client, "en", "understanding-fft", "198.51.100.11");
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
    public async Task ArticleView_ForDraftLocalization_ReturnsNotFound()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedContent(factory.Services);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/public/articles/en/future-draft/views", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListsAndTaxonomy_IncludeOnlyPublishedArticlesAndSupportCombinedFilters()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedContent(factory.Services);
        using var client = factory.CreateClient();

        var filtered = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en&topic=signal-processing&series=fourier-notes&tag=.NET");
        var topics = await client.GetFromJsonAsync<IReadOnlyList<PublicTopicDto>>(
            "/api/public/topics/en");
        var series = await client.GetFromJsonAsync<IReadOnlyList<PublicSeriesListItemDto>>(
            "/api/public/series/en");
        var tags = await client.GetFromJsonAsync<IReadOnlyList<PublicTagDto>>(
            "/api/public/tags/en");
        var seriesDetails = await client.GetFromJsonAsync<PublicSeriesDetailsDto>(
            "/api/public/series/en/fourier-notes");

        var article = Assert.Single(filtered!);
        Assert.Equal("understanding-fft", article.Slug);
        Assert.Equal("Signal processing", Assert.Single(article.Topics).DisplayName);
        Assert.Equal("Fourier notes", Assert.Single(article.Series).DisplayName);
        Assert.Equal([".NET"], article.Tags);

        Assert.Equal(1, Assert.Single(topics!).ArticleCount);
        Assert.Equal(1, Assert.Single(series!).ArticleCount);
        Assert.Equal(1, Assert.Single(tags!).ArticleCount);
        Assert.Equal("understanding-fft", Assert.Single(seriesDetails!.Articles).Slug);

        var allEnglish = await client.GetFromJsonAsync<IReadOnlyList<PublicArticleListItemDto>>(
            "/api/public/articles?languageCode=en");
        Assert.Single(allEnglish!);
    }

    [Fact]
    public async Task ArticlePdfExport_ForPublishedLocalization_QueuesExport()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedContent(factory.Services);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/public/articles/en/understanding-fft/pdf-exports?lineHeight=1.5&blockSpacing=0.4",
            content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var export = await response.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        Assert.NotNull(export);
        Assert.Equal("queued", export.Status);

        var status = await client.GetFromJsonAsync<PdfExportStatusDto>(
            $"/api/public/pdf-exports/{export.Id}");
        Assert.Equal("queued", status?.Status);
    }

    [Fact]
    public async Task ArticlePdf_ForDraft_DoesNotCallRenderer()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedContent(factory.Services);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/public/articles/en/future-draft/pdf-exports", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArticlePdfExport_WhenNotReady_ReturnsConflictOnDownload()
    {
        await using var factory = new AuthWebApplicationFactory();
        await SeedContent(factory.Services);
        using var client = factory.CreateClient();

        var created = await client.PostAsync(
            "/api/public/articles/en/understanding-fft/pdf-exports",
            content: null);
        var export = await created.Content.ReadFromJsonAsync<PdfExportStatusDto>();
        var response = await client.GetAsync($"/api/public/pdf-exports/{export!.Id}/download");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("pdf_export_not_ready", error?.Code);
    }

    private static async Task SeedContent(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow.AddDays(-1);

        var article = Article.Create(
            "test-owner",
            "en",
            now,
            "Understanding FFT",
            "A practical introduction",
            "**safe** <script>alert('xss')</script>",
            "understanding-fft");
        article.PublishLocalization("en", now);
        article.AddLocalization(
            "ru",
            now,
            "Как работает FFT",
            "Практическое введение",
            "**безопасно**",
            "kak-rabotaet-fft");
        article.PublishLocalization("ru", now);

        var topic = Topic.Create(
            "en",
            "Signal processing",
            "signal-processing",
            null,
            now);
        topic.AddLocalization(
            "ru",
            "Обработка сигналов",
            "obrabotka-signalov",
            null,
            now);
        var series = SeriesAggregate.Create(
            "en",
            "Fourier notes",
            "fourier-notes",
            null,
            now);
        series.AddLocalization(
            "ru",
            "Заметки о Фурье",
            "zametki-o-fure",
            null,
            now);
        var tag = Tag.Create(".NET");

        article.AssignTopic(topic, now);
        article.AssignTag(tag, now);
        series.AddArticle(article, 1, now);

        var draft = Article.Create(
            "test-owner",
            "en",
            now,
            "Future draft",
            null,
            "Not published",
            "future-draft");
        draft.AssignTopic(topic, now);
        draft.AssignTag(tag, now);
        series.AddArticle(draft, 2, now);

        dbContext.AddRange(article, draft, topic, series, tag);
        await dbContext.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> PostArticleViewAsync(
        HttpClient client,
        string languageCode,
        string slug,
        string forwardedFor)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/public/articles/{languageCode}/{slug}/views");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

}
