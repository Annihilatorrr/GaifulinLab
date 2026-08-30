namespace GaifulinLab.Domain.Articles;

public sealed class ArticleTag
{
    private ArticleTag()
    {
    }

    internal ArticleTag(Guid articleId, Guid tagId)
    {
        ArticleId = articleId;
        TagId = tagId;
    }

    public Guid ArticleId { get; private set; }

    public Guid TagId { get; private set; }
}
