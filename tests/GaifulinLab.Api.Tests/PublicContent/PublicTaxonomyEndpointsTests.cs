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

        var topics = await client.GetFromJsonAsync<IReadOnlyList<PublicTopicDto>>("/api/public/topics/en");
        var series = await client.GetFromJsonAsync<IReadOnlyList<PublicSeriesListItemDto>>("/api/public/series/en");
        var tags = await client.GetFromJsonAsync<IReadOnlyList<PublicTagDto>>("/api/public/tags/en");
        var seriesDetails = await client.GetFromJsonAsync<PublicSeriesDetailsDto>("/api/public/series/en/fourier-notes");

        Assert.Equal(1, Assert.Single(topics!).ArticleCount);
        Assert.Equal(1, Assert.Single(series!).ArticleCount);
        Assert.Equal(1, Assert.Single(tags!).ArticleCount);
        Assert.Equal("understanding-fft", Assert.Single(seriesDetails!.Articles).Slug);
        Assert.Equal("Test Author", Assert.Single(seriesDetails.Articles).AuthorDisplayName);
    }
}
