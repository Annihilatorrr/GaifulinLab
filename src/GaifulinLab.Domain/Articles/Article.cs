using GaifulinLab.Domain.Common;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;

namespace GaifulinLab.Domain.Articles;

public sealed class Article
{
    private readonly List<ArticleLocalization> _localizations = [];
    private readonly List<ArticleTopic> _topics = [];
    private readonly List<ArticleTag> _tags = [];

    private Article()
    {
    }

    private Article(DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        CreatedAt = DomainRules.AsUtc(createdAt);
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ArticleLocalization> Localizations => _localizations;

    public IReadOnlyCollection<ArticleTopic> Topics => _topics;

    public IReadOnlyCollection<ArticleTag> Tags => _tags;

    public static Article Create(
        string languageCode,
        DateTimeOffset createdAt,
        string? title = null,
        string? summary = null,
        string? markdown = null,
        string? slug = null)
    {
        var article = new Article(createdAt);
        article.AddLocalization(languageCode, createdAt, title, summary, markdown, slug);
        return article;
    }

    public ArticleLocalization AddLocalization(
        string languageCode,
        DateTimeOffset createdAt,
        string? title = null,
        string? summary = null,
        string? markdown = null,
        string? slug = null)
    {
        var normalizedLanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        if (_localizations.Any(localization => localization.LanguageCode == normalizedLanguageCode))
        {
            throw new InvalidOperationException($"The article already has a '{normalizedLanguageCode}' localization.");
        }

        var localization = new ArticleLocalization(
            Id,
            normalizedLanguageCode,
            title,
            summary,
            markdown,
            slug,
            createdAt);

        _localizations.Add(localization);
        Touch(createdAt);
        return localization;
    }

    public ArticleLocalization? FindLocalization(string languageCode)
    {
        var normalizedLanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        return _localizations.SingleOrDefault(localization => localization.LanguageCode == normalizedLanguageCode);
    }

    public void UpdateLocalization(
        string languageCode,
        string? title,
        string? summary,
        string? markdown,
        string? slug,
        DateTimeOffset updatedAt)
    {
        var localization = GetRequiredLocalization(languageCode);
        localization.UpdateContent(title, summary, markdown, slug, updatedAt);
        Touch(updatedAt);
    }

    public void PublishLocalization(string languageCode, DateTimeOffset publishedAt)
    {
        GetRequiredLocalization(languageCode).Publish(publishedAt);
        Touch(publishedAt);
    }

    public void UnpublishLocalization(string languageCode, DateTimeOffset updatedAt)
    {
        GetRequiredLocalization(languageCode).Unpublish(updatedAt);
        Touch(updatedAt);
    }

    public void AssignTopic(Topic topic, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(topic);
        if (_topics.All(link => link.TopicId != topic.Id))
        {
            _topics.Add(new ArticleTopic(Id, topic.Id));
            Touch(updatedAt);
        }
    }

    public void AssignTag(Tag tag, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (_tags.All(link => link.TagId != tag.Id))
        {
            _tags.Add(new ArticleTag(Id, tag.Id));
            Touch(updatedAt);
        }
    }

    private ArticleLocalization GetRequiredLocalization(string languageCode) =>
        FindLocalization(languageCode)
        ?? throw new InvalidOperationException($"The article does not have a '{languageCode}' localization.");

    private void Touch(DateTimeOffset updatedAt) => UpdatedAt = DomainRules.AsUtc(updatedAt);
}
