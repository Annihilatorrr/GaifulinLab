namespace GaifulinLab.Contracts.Taxonomy;

public sealed record SeriesLocalizationDto(
    Guid Id,
    string LanguageCode,
    string Title,
    string Slug,
    string? Description);
