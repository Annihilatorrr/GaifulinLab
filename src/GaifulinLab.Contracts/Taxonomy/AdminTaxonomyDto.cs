namespace GaifulinLab.Contracts.Taxonomy;

public sealed record AdminTaxonomyDto(
    IReadOnlyList<AdminTopicDto> Topics,
    IReadOnlyList<AdminSeriesDto> Series,
    IReadOnlyList<AdminTagDto> Tags);
