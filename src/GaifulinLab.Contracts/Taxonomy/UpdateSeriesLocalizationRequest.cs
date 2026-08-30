namespace GaifulinLab.Contracts.Taxonomy;

public sealed record UpdateSeriesLocalizationRequest(
    string Title,
    string Slug,
    string? Description);
