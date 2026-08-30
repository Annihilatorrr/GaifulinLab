using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Common;

namespace GaifulinLab.Domain.Series;

public sealed class Series
{
    private readonly List<SeriesLocalization> _localizations = [];
    private readonly List<ArticleSeries> _articles = [];

    private Series()
    {
    }

    private Series(DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        CreatedAt = DomainRules.AsUtc(createdAt);
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<SeriesLocalization> Localizations => _localizations;

    public IReadOnlyCollection<ArticleSeries> Articles => _articles;

    public static Series Create(
        string languageCode,
        string title,
        string slug,
        string? description,
        DateTimeOffset createdAt)
    {
        var series = new Series(createdAt);
        series.AddLocalization(languageCode, title, slug, description, createdAt);
        return series;
    }

    public SeriesLocalization AddLocalization(
        string languageCode,
        string title,
        string slug,
        string? description,
        DateTimeOffset updatedAt)
    {
        var normalizedLanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        if (_localizations.Any(localization => localization.LanguageCode == normalizedLanguageCode))
        {
            throw new InvalidOperationException($"The series already has a '{normalizedLanguageCode}' localization.");
        }

        var localization = new SeriesLocalization(Id, normalizedLanguageCode, title, slug, description);
        _localizations.Add(localization);
        UpdatedAt = DomainRules.AsUtc(updatedAt);
        return localization;
    }

    public SeriesLocalization? FindLocalization(string languageCode)
    {
        var normalizedLanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        return _localizations.SingleOrDefault(localization => localization.LanguageCode == normalizedLanguageCode);
    }

    public void UpdateLocalization(
        string languageCode,
        string title,
        string slug,
        string? description,
        DateTimeOffset updatedAt)
    {
        var localization = FindLocalization(languageCode)
            ?? throw new InvalidOperationException($"The series does not have a '{languageCode}' localization.");

        localization.Update(title, slug, description);
        UpdatedAt = DomainRules.AsUtc(updatedAt);
    }

    public ArticleSeries AddArticle(Article article, int position, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(article);

        if (_articles.Any(link => link.ArticleId == article.Id))
        {
            throw new InvalidOperationException("The article is already part of this series.");
        }

        if (_articles.Any(link => link.Position == position))
        {
            throw new InvalidOperationException($"Series position {position} is already occupied.");
        }

        var link = new ArticleSeries(article.Id, Id, position);
        _articles.Add(link);
        UpdatedAt = DomainRules.AsUtc(updatedAt);
        return link;
    }

    public void RemoveArticle(Guid articleId, DateTimeOffset updatedAt)
    {
        var removed = _articles.RemoveAll(link => link.ArticleId == articleId);
        if (removed > 0)
        {
            UpdatedAt = DomainRules.AsUtc(updatedAt);
        }
    }

    public ArticleSeries SetArticle(Article article, int position, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(article);

        var existingLink = _articles.SingleOrDefault(link => link.ArticleId == article.Id);
        if (_articles.Any(link => link.ArticleId != article.Id && link.Position == position))
        {
            throw new InvalidOperationException($"Series position {position} is already occupied.");
        }

        if (existingLink is null)
        {
            return AddArticle(article, position, updatedAt);
        }

        existingLink.ChangePosition(position);
        UpdatedAt = DomainRules.AsUtc(updatedAt);
        return existingLink;
    }
}
