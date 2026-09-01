using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Application.Common;
using GaifulinLab.Application.Pdf;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public async Task ArticlePdf_ForPublishedLocalization_ReturnsDownload()
    {
        await using var factory = new AuthWebApplicationFactory();
        var renderer = new StubArticlePdfRenderer("%PDF-1.7\narticle"u8.ToArray());
        await using var pdfFactory = WithPdfRenderer(factory, renderer);
        await SeedContent(pdfFactory.Services);
        using var client = pdfFactory.CreateClient();

        var response = await client.GetAsync(
            "/api/public/articles/en/understanding-fft/pdf?lineHeight=1.5&blockSpacing=0.4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("understanding-fft.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("%PDF-1.7\narticle"u8.ToArray(), await response.Content.ReadAsByteArrayAsync());
        var request = Assert.Single(renderer.Requests);
        Assert.Equal("en", request.LanguageCode);
        Assert.Equal("understanding-fft", request.Slug);
        Assert.Equal(new ArticleTypography(1.5m, 0.4m), request.Typography);
    }

    [Fact]
    public async Task ArticlePdf_ForDraft_DoesNotCallRenderer()
    {
        await using var factory = new AuthWebApplicationFactory();
        var renderer = new StubArticlePdfRenderer("%PDF-1.7"u8.ToArray());
        await using var pdfFactory = WithPdfRenderer(factory, renderer);
        await SeedContent(pdfFactory.Services);
        using var client = pdfFactory.CreateClient();

        var response = await client.GetAsync("/api/public/articles/en/future-draft/pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(renderer.Requests);
    }

    [Fact]
    public async Task ArticlePdf_WhenRendererFails_ReturnsServiceUnavailable()
    {
        await using var factory = new AuthWebApplicationFactory();
        var renderer = new StubArticlePdfRenderer(
            exception: new PdfRenderingException("The article PDF renderer is unavailable."));
        await using var pdfFactory = WithPdfRenderer(factory, renderer);
        await SeedContent(pdfFactory.Services);
        using var client = pdfFactory.CreateClient();

        var response = await client.GetAsync(
            "/api/public/articles/en/understanding-fft/pdf");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("pdf_renderer_unavailable", error?.Code);
    }

    private static WebApplicationFactory<Program> WithPdfRenderer(
        AuthWebApplicationFactory factory,
        IArticlePdfRenderer renderer) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IArticlePdfRenderer>();
            services.AddSingleton(renderer);
        }));

    private static async Task SeedContent(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow.AddDays(-1);

        var article = Article.Create(
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

    private sealed class StubArticlePdfRenderer(
        byte[]? pdf = null,
        Exception? exception = null) : IArticlePdfRenderer
    {
        public List<(string LanguageCode, string Slug, ArticleTypography Typography)> Requests { get; } = [];

        public Task<byte[]> RenderAsync(
            string languageCode,
            string slug,
            ArticleTypography typography,
            CancellationToken cancellationToken)
        {
            Requests.Add((languageCode, slug, typography));
            return exception is null
                ? Task.FromResult(pdf ?? "%PDF-1.7"u8.ToArray())
                : Task.FromException<byte[]>(exception);
        }
    }
}
