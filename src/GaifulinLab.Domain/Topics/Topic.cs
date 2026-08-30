using GaifulinLab.Domain.Common;

namespace GaifulinLab.Domain.Topics;

public sealed class Topic
{
    private readonly List<TopicLocalization> _localizations = [];

    private Topic()
    {
    }

    private Topic(DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        CreatedAt = DomainRules.AsUtc(createdAt);
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<TopicLocalization> Localizations => _localizations;

    public static Topic Create(
        string languageCode,
        string name,
        string slug,
        string? description,
        DateTimeOffset createdAt)
    {
        var topic = new Topic(createdAt);
        topic.AddLocalization(languageCode, name, slug, description, createdAt);
        return topic;
    }

    public TopicLocalization AddLocalization(
        string languageCode,
        string name,
        string slug,
        string? description,
        DateTimeOffset updatedAt)
    {
        var normalizedLanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        if (_localizations.Any(localization => localization.LanguageCode == normalizedLanguageCode))
        {
            throw new InvalidOperationException($"The topic already has a '{normalizedLanguageCode}' localization.");
        }

        var localization = new TopicLocalization(Id, normalizedLanguageCode, name, slug, description);
        _localizations.Add(localization);
        UpdatedAt = DomainRules.AsUtc(updatedAt);
        return localization;
    }

    public TopicLocalization? FindLocalization(string languageCode)
    {
        var normalizedLanguageCode = DomainRules.NormalizeLanguageCode(languageCode);
        return _localizations.SingleOrDefault(localization => localization.LanguageCode == normalizedLanguageCode);
    }

    public void UpdateLocalization(
        string languageCode,
        string name,
        string slug,
        string? description,
        DateTimeOffset updatedAt)
    {
        var localization = FindLocalization(languageCode)
            ?? throw new InvalidOperationException($"The topic does not have a '{languageCode}' localization.");

        localization.Update(name, slug, description);
        UpdatedAt = DomainRules.AsUtc(updatedAt);
    }
}
