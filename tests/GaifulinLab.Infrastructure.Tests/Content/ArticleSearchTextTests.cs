using GaifulinLab.Infrastructure.Content;
using GaifulinLab.Infrastructure.Persistence;
using GaifulinLab.Domain.Articles;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Infrastructure.Tests.Content;

public sealed class ArticleSearchTextTests
{
    [Fact]
    public void Extract_KeepsVisibleWordsAndCodeWithoutMarkdownOrLinkDestinations()
    {
        var text = ArticleSearchText.Extract("# Title\n\nRead **carefully** [guide](https://secret-destination.test)\n\n```csharp\nvar example = 1;\n```\n\n<script>alert('bad')</script>");
        Assert.Contains("Title", text);
        Assert.Contains("carefully", text);
        Assert.Contains("guide", text);
        Assert.Contains("example", text);
        Assert.DoesNotContain("secret-destination", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("<script>", text);
    }

    [Fact]
    public void ToPrefixTsQuery_UsesEveryNormalizedWordAsASafePrefix()
    {
        var query = ArticleSearchText.ToPrefixTsQuery(ArticleSearchText.NormalizeQuery("C++ lesson-22"));

        Assert.Equal("glcpp:* & lesson:* & 22:*", query);
    }

    [Fact]
    public async Task SavingContent_RefreshesSearchTextAndReadingTimeWithoutExtraEditVersion()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var article = Article.Create("owner", "en", DateTimeOffset.UtcNow, "Title", null, "Old body", "title");
        db.Articles.Add(article);
        await db.SaveChangesAsync();
        Assert.Equal("Old body", article.Localizations.Single().SearchText);
        article.UpdateLocalization("en", "Title", null, string.Join(' ', Enumerable.Repeat("word", 401)), "title", DateTimeOffset.UtcNow);
        var version = article.Localizations.Single().Version;
        await db.SaveChangesAsync();
        Assert.Equal(3, article.Localizations.Single().ReadingMinutes);
        Assert.DoesNotContain("Old", article.Localizations.Single().SearchText);
        Assert.Equal(version, article.Localizations.Single().Version);
    }
}
