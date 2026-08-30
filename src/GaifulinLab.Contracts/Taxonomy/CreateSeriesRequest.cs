namespace GaifulinLab.Contracts.Taxonomy;

public sealed record CreateSeriesRequest(
    string LanguageCode,
    string Title,
    string Slug,
    string? Description);
