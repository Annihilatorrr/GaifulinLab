using System.Net.Http.Json;
using GaifulinLab.Api.Tests.Authentication;
using GaifulinLab.Contracts.Taxonomy;

namespace GaifulinLab.Api.Tests.PublicContent;

public sealed class PublicTaxonomyEndpointsTests
{
    [Fact]
    public async Task Taxonomy_WhenDatabaseIsEmpty_ReturnEmptyCollections()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateClient();

        var topics = await client.GetFromJsonAsync<IReadOnlyList<PublicTopicDto>>("/api/public/topics/en");
        var series = await client.GetFromJsonAsync<IReadOnlyList<PublicSeriesListItemDto>>("/api/public/series/en");
        var tags = await client.GetFromJsonAsync<IReadOnlyList<PublicTagDto>>("/api/public/tags/en");

        Assert.Empty(topics!);
        Assert.Empty(series!);
        Assert.Empty(tags!);
    }

    [Fact]
    public async Task Taxonomy_IncludesOnlyPublishedArticles()
    {
        await using var factory = new AuthWebApplicationFactory();
        await PublicContentTestData.SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        var topicsResponse = await client.GetAsync("/api/public/topics/en");
        var seriesResponse = await client.GetAsync("/api/public/series/en");
        var tagsResponse = await client.GetAsync("/api/public/tags/en");
        var seriesDetailsResponse = await client.GetAsync("/api/public/series/en/fourier-notes");
        var topics = await topicsResponse.Content.ReadFromJsonAsync<IReadOnlyList<PublicTopicDto>>();
        var series = await seriesResponse.Content.ReadFromJsonAsync<IReadOnlyList<PublicSeriesListItemDto>>();
        var tags = await tagsResponse.Content.ReadFromJsonAsync<IReadOnlyList<PublicTagDto>>();
        var seriesDetailsPayload = await seriesDetailsResponse.Content.ReadAsStringAsync();
        var seriesDetails = System.Text.Json.JsonSerializer.Deserialize<PublicSeriesDetailsDto>(
            seriesDetailsPayload,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.True(topicsResponse.Headers.CacheControl?.NoStore);
        Assert.True(seriesResponse.Headers.CacheControl?.NoStore);
        Assert.True(tagsResponse.Headers.CacheControl?.NoStore);
        Assert.True(seriesDetailsResponse.Headers.CacheControl?.NoStore);
        Assert.Equal(1, Assert.Single(topics!).ArticleCount);
        Assert.Equal(1, Assert.Single(series!).ArticleCount);
        Assert.Equal(1, Assert.Single(tags!).ArticleCount);
        var seriesArticle = Assert.Single(seriesDetails!.Articles);
        Assert.Equal("understanding-fft", seriesArticle.Slug);
        Assert.Equal("Signal processing", Assert.Single(seriesArticle.Topics!).DisplayName);
        Assert.Equal([".NET"], seriesArticle.Tags);
        Assert.Equal(1, seriesArticle.ReadingMinutes);
        Assert.NotNull(seriesArticle.LastEditedAt);
        Assert.DoesNotContain("authorDisplayName", seriesDetailsPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Test Author", seriesDetailsPayload);
    }
}
