using GaifulinLab.Domain.Articles;

namespace GaifulinLab.Domain.Tests.Articles;

public sealed class ArticleViewTests
{
    [Fact]
    public void Create_NormalizesHashAndTimestamp()
    {
        var articleId = Guid.NewGuid();
        var hash = new string('A', 64);
        var firstViewedAt = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(3));

        var view = ArticleView.Create(articleId, hash, firstViewedAt);

        Assert.Equal(articleId, view.ArticleId);
        Assert.Equal(hash.ToLowerInvariant(), view.VisitorHash);
        Assert.Equal(firstViewedAt.ToUniversalTime(), view.FirstViewedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-sha256-hash")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaz")]
    public void Create_RejectsInvalidVisitorHash(string hash)
    {
        Assert.Throws<ArgumentException>(() =>
            ArticleView.Create(Guid.NewGuid(), hash, DateTimeOffset.UtcNow));
    }
}
