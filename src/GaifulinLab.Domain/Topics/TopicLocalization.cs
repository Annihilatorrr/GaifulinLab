using GaifulinLab.Domain.Common;

namespace GaifulinLab.Domain.Topics;

public sealed class TopicLocalization
{
    private TopicLocalization()
    {
    }

    internal TopicLocalization(
        Guid topicId,
        string languageCode,
        string name,
        string slug,
        string? description)
    {
        Id = Guid.NewGuid();
        TopicId = topicId;
        LanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        Update(name, slug, description);
    }

    public Guid Id { get; private set; }

    public Guid TopicId { get; private set; }

    public string LanguageCode { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    internal void Update(string name, string slug, string? description)
    {
        Name = DomainRules.RequireTrimmed(name, nameof(name));
        Slug = DomainRules.NormalizeSlug(slug);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}
