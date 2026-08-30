namespace GaifulinLab.Contracts.Taxonomy;

public sealed record CreateTopicRequest(
    string LanguageCode,
    string Name,
    string Slug,
    string? Description);
