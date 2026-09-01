using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Media;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Domain.Series;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Infrastructure.Tests.Persistence;

public sealed class AppDbContextModelTests
{
    [Fact]
    public void Model_ContainsEveryDomainEntity()
    {
        using var context = CreateContext();
        var expectedEntityTypes = new[]
        {
            typeof(Article),
            typeof(ArticleLocalization),
            typeof(ArticleTopic),
            typeof(ArticleTag),
            typeof(Topic),
            typeof(TopicLocalization),
            typeof(SeriesAggregate),
            typeof(SeriesLocalization),
            typeof(ArticleSeries),
            typeof(Tag),
            typeof(MediaAsset),
            typeof(PdfExportJob)
        };

        Assert.All(expectedEntityTypes, entityType =>
            Assert.NotNull(context.Model.FindEntityType(entityType)));
    }

    [Fact]
    public void Model_ContainsRequiredUniqueIndexes()
    {
        using var context = CreateContext();

        AssertUniqueIndex(
            context.Model.FindEntityType(typeof(ArticleLocalization))!,
            nameof(ArticleLocalization.ArticleId),
            nameof(ArticleLocalization.LanguageCode));
        AssertUniqueIndex(
            context.Model.FindEntityType(typeof(ArticleLocalization))!,
            nameof(ArticleLocalization.LanguageCode),
            nameof(ArticleLocalization.Slug));
        AssertUniqueIndex(
            context.Model.FindEntityType(typeof(ArticleSeries))!,
            nameof(ArticleSeries.SeriesId),
            nameof(ArticleSeries.Position));
        AssertUniqueIndex(
            context.Model.FindEntityType(typeof(Tag))!,
            nameof(Tag.NormalizedName));
    }

    [Fact]
    public void InitialMigration_GeneratesExpectedPostgresConstraints()
    {
        using var context = CreateContext();
        var migrator = context.GetService<IMigrator>();

        var script = migrator.GenerateScript();

        Assert.Contains("article_localizations", script);
        Assert.Contains("ux_article_localizations_language_slug", script);
        Assert.Contains("ux_article_series_series_position", script);
        Assert.Contains("ck_article_localizations_status", script);
        Assert.Contains("ck_article_series_position_positive", script);
        Assert.Contains("media_assets", script);
        Assert.Contains("pdf_export_jobs", script);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=gaifulinlab_tests;Username=test;Password=test")
            .Options;

        return new AppDbContext(options);
    }

    private static void AssertUniqueIndex(IReadOnlyEntityType entityType, params string[] propertyNames)
    {
        var index = Assert.Single(entityType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));

        Assert.True(index.IsUnique);
    }
}
