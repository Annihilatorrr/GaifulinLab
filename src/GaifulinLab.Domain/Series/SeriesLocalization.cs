using GaifulinLab.Domain.Common;

namespace GaifulinLab.Domain.Series;

public sealed class SeriesLocalization
{
    private SeriesLocalization()
    {
    }

    internal SeriesLocalization(
        Guid seriesId,
        string languageCode,
        string title,
        string slug,
        string? description)
    {
        Id = Guid.NewGuid();
        SeriesId = seriesId;
        LanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        Update(title, slug, description);
    }

    public Guid Id { get; private set; }

    public Guid SeriesId { get; private set; }

    public string LanguageCode { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    internal void Update(string title, string slug, string? description)
    {
        Title = DomainRules.RequireTrimmed(title, nameof(title));
        Slug = DomainRules.NormalizeSlug(slug);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}
