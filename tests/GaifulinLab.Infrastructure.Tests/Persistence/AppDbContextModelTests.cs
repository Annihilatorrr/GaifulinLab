using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Media;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Domain.Series;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
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
            typeof(ArticleView),
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
        AssertPrimaryKey(
            context.Model.FindEntityType(typeof(ArticleView))!,
            nameof(ArticleView.ArticleId),
            nameof(ArticleView.VisitorHash));
        AssertUniqueIndex(
            context.Model.FindEntityType(typeof(ArticleSeries))!,
            nameof(ArticleSeries.SeriesId),
            nameof(ArticleSeries.Position));
        AssertUniqueIndex(
            context.Model.FindEntityType(typeof(Tag))!,
            nameof(Tag.NormalizedName));

        var articleType = context.Model.FindEntityType(typeof(Article))!;
        Assert.Contains(articleType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(Article.OwnerUserId), nameof(Article.UpdatedAt)]));
        Assert.Contains(articleType.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name).SequenceEqual([nameof(Article.OwnerUserId)])
            && foreignKey.PrincipalEntityType.ClrType == typeof(ApplicationUser)
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Model_ContainsAspNetCoreIdentityUsersAndRoles()
    {
        using var context = CreateContext();

        Assert.NotNull(context.Model.FindEntityType(typeof(ApplicationUser)));
        Assert.NotNull(context.Model.FindEntityType(typeof(IdentityRole)));
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
        Assert.Contains("article_views", script);
        Assert.Contains("AspNetUsers", script);
        Assert.Contains("AspNetRoles", script);
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

    private static void AssertPrimaryKey(IReadOnlyEntityType entityType, params string[] propertyNames) =>
        Assert.Equal(
            propertyNames,
            entityType.FindPrimaryKey()!.Properties.Select(property => property.Name));
}
