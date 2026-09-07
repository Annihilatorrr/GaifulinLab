using GaifulinLab.Domain.Articles;

namespace GaifulinLab.Domain.Tests.Articles;

public sealed class ArticleTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_AddsDraftLocalizationAndNormalizesLanguage()
    {
        var article = Article.Create("test-owner", " EN ", CreatedAt);

        var localization = Assert.Single(article.Localizations);
        Assert.Equal("test-owner", article.OwnerUserId);
        Assert.Equal("en", localization.LanguageCode);
        Assert.Equal(PublicationStatus.Draft, localization.Status);
        Assert.Null(localization.PublishedAt);
    }

    [Fact]
    public void AddLocalization_RejectsDuplicateLanguage()
    {
        var article = Article.Create("test-owner", "en", CreatedAt);

        var exception = Assert.Throws<InvalidOperationException>(
            () => article.AddLocalization("EN", CreatedAt));

        Assert.Contains("already has", exception.Message);
    }

    [Fact]
    public void PublishingOneLocalization_DoesNotPublishAnother()
    {
        var article = Article.Create(
            "test-owner",
            "en",
            CreatedAt,
            "Understanding FFT",
            markdown: "FFT content",
            slug: "Understanding FFT");
        article.AddLocalization(
            "ru",
            CreatedAt,
            "Как работает FFT",
            markdown: "Русский текст",
            slug: "kak-rabotaet-fft");

        article.PublishLocalization("en", CreatedAt.AddDays(1));

        Assert.Equal(PublicationStatus.Published, article.FindLocalization("en")!.Status);
        Assert.Equal(PublicationStatus.Draft, article.FindLocalization("ru")!.Status);
        Assert.Equal("understanding-fft", article.FindLocalization("en")!.Slug);
    }

    [Theory]
    [InlineData(null, "content", "article-slug")]
    [InlineData("Title", null, "article-slug")]
    [InlineData("Title", "content", null)]
    public void Publish_RejectsIncompleteDraft(string? title, string? markdown, string? slug)
    {
        var article = Article.Create("test-owner", "en", CreatedAt, title, markdown: markdown, slug: slug);

        Assert.Throws<InvalidOperationException>(
            () => article.PublishLocalization("en", CreatedAt.AddDays(1)));
    }

    [Fact]
    public void Unpublish_PreservesOriginalPublicationDate()
    {
        var publishedAt = CreatedAt.AddDays(1);
        var article = Article.Create("test-owner", "en", CreatedAt, "Title", markdown: "Content", slug: "article");
        article.PublishLocalization("en", publishedAt);

        article.UnpublishLocalization("en", CreatedAt.AddDays(2));

        var localization = article.FindLocalization("en")!;
        Assert.Equal(PublicationStatus.Unpublished, localization.Status);
        Assert.Equal(publishedAt, localization.PublishedAt);
    }

    [Fact]
    public void Publish_AllowsDraftAndUnpublishedButNotPublished()
    {
        var article = Article.Create("test-owner", "en", CreatedAt, "Title", markdown: "Content", slug: "article");

        article.PublishLocalization("en", CreatedAt.AddDays(1));
        Assert.Throws<InvalidOperationException>(() => article.PublishLocalization("en", CreatedAt.AddDays(2)));

        article.UnpublishLocalization("en", CreatedAt.AddDays(3));
        article.PublishLocalization("en", CreatedAt.AddDays(4));

        var localization = article.FindLocalization("en")!;
        Assert.Equal(PublicationStatus.Published, localization.Status);
        Assert.Equal(CreatedAt.AddDays(1), localization.PublishedAt);
    }

    [Fact]
    public void Unpublish_RejectsDraftAndDoesNotReturnToDraft()
    {
        var article = Article.Create("test-owner", "en", CreatedAt, "Title", markdown: "Content", slug: "article");

        Assert.Throws<InvalidOperationException>(() => article.UnpublishLocalization("en", CreatedAt.AddDays(1)));

        article.PublishLocalization("en", CreatedAt.AddDays(2));
        article.UnpublishLocalization("en", CreatedAt.AddDays(3));

        Assert.Equal(PublicationStatus.Unpublished, article.FindLocalization("en")!.Status);
    }

    [Fact]
    public void Delete_PreservesContentAndRejectsFurtherChanges()
    {
        var article = Article.Create("test-owner", "en", CreatedAt, "Title", markdown: "Content", slug: "article");

        article.Delete(CreatedAt.AddDays(1));

        Assert.True(article.IsDeleted);
        Assert.Equal(CreatedAt.AddDays(1), article.DeletedAt);
        Assert.Equal("Content", article.FindLocalization("en")!.Markdown);
        Assert.Throws<InvalidOperationException>(() => article.UpdateLocalization(
            "en", "Updated", null, "Updated", "updated", CreatedAt.AddDays(2)));
        Assert.Throws<InvalidOperationException>(() => article.Delete(CreatedAt.AddDays(2)));
    }

    [Fact]
    public void UpdateLocalization_TracksLastContentEditSeparatelyFromPublicationChanges()
    {
        var article = Article.Create("test-owner", "en", CreatedAt, "Title", markdown: "Content", slug: "article");
        var localization = article.FindLocalization("en")!;
        Assert.Equal(CreatedAt, localization.LastEditedAt);

        article.PublishLocalization("en", CreatedAt.AddDays(1));
        Assert.Equal(CreatedAt, localization.LastEditedAt);

        article.UpdateLocalization("en", "Updated", null, "Updated content", "updated", CreatedAt.AddDays(2));
        Assert.Equal(CreatedAt.AddDays(2), localization.LastEditedAt);
    }

    [Fact]
    public void UpdatePublishedLocalization_RejectsIncompleteContentWithoutChangingIt()
    {
        var article = Article.Create("test-owner", "en", CreatedAt, "Title", markdown: "Content", slug: "article");
        article.PublishLocalization("en", CreatedAt.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => article.UpdateLocalization(
            "en",
            string.Empty,
            null,
            "Updated content",
            "updated-article",
            CreatedAt.AddDays(2)));

        var localization = article.FindLocalization("en")!;
        Assert.Equal("Title", localization.Title);
        Assert.Equal("Content", localization.Markdown);
        Assert.Equal("article", localization.Slug);
    }

    [Fact]
    public void FindLocalization_DoesNotFallBackToAnotherLanguage()
    {
        var article = Article.Create("test-owner", "en", CreatedAt);

        Assert.Null(article.FindLocalization("ru"));
    }
}
