namespace GaifulinLab.Contracts.Taxonomy;

public sealed record UpdateTopicLocalizationRequest(
    string Name,
    string Slug,
    string? Description);
