using GaifulinLab.Contracts.Articles;
using MediatR;

namespace GaifulinLab.Application.Articles.UpdateArticleTaxonomy;

public sealed record UpdateArticleTaxonomyCommand(
    Guid ArticleId,
    string UserId,
    IReadOnlyList<Guid> TopicIds,
    IReadOnlyList<SeriesAssignmentRequest> Series,
    IReadOnlyList<string> Tags) : IRequest;
