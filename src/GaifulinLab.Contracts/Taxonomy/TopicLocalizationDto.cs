namespace GaifulinLab.Contracts.Taxonomy;

public sealed record TopicLocalizationDto(
    Guid Id,
    string LanguageCode,
    string Name,
    string Slug,
    string? Description);
