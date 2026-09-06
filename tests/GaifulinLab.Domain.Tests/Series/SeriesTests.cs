using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Series;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Domain.Tests.SeriesModel;

public sealed class SeriesTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddArticle_RejectsOccupiedPosition()
    {
        var series = SeriesAggregate.Create("en", "DSP Basics", "dsp-basics", null, CreatedAt);
        series.AddArticle(Article.Create("test-owner", "en", CreatedAt), 1, CreatedAt);

        Assert.Throws<InvalidOperationException>(
            () => series.AddArticle(Article.Create("test-owner", "en", CreatedAt), 1, CreatedAt));
    }

    [Fact]
    public void AddArticle_RejectsNonPositivePosition()
    {
        var series = SeriesAggregate.Create("en", "DSP Basics", "dsp-basics", null, CreatedAt);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => series.AddArticle(Article.Create("test-owner", "en", CreatedAt), 0, CreatedAt));
    }
}
