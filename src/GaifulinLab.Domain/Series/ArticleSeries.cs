namespace GaifulinLab.Domain.Series;

public sealed class ArticleSeries
{
    private ArticleSeries()
    {
    }

    internal ArticleSeries(Guid articleId, Guid seriesId, int position)
    {
        if (position <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Series position must be greater than zero.");
        }

        ArticleId = articleId;
        SeriesId = seriesId;
        Position = position;
    }

    public Guid ArticleId { get; private set; }

    public Guid SeriesId { get; private set; }

    public int Position { get; private set; }

    internal void ChangePosition(int position)
    {
        if (position <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Series position must be greater than zero.");
        }

        Position = position;
    }
}
