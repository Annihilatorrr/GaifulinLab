namespace GaifulinLab.Domain.Articles;

public sealed class ArticleTopic
{
    private ArticleTopic()
    {
    }

    internal ArticleTopic(Guid articleId, Guid topicId)
    {
        ArticleId = articleId;
        TopicId = topicId;
    }

    public Guid ArticleId { get; private set; }

    public Guid TopicId { get; private set; }
}
