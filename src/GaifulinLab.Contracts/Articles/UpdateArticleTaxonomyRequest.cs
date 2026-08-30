namespace GaifulinLab.Contracts.Articles;

public sealed record UpdateArticleTaxonomyRequest(
    IReadOnlyList<Guid> TopicIds,
    IReadOnlyList<SeriesAssignmentRequest> Series,
    IReadOnlyList<string> Tags);
