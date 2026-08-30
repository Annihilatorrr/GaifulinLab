using GaifulinLab.Contracts.Articles;
using MediatR;

namespace GaifulinLab.Application.Articles.UpdateArticleTaxonomy;

public sealed record UpdateArticleTaxonomyCommand(
    Guid ArticleId,
    IReadOnlyList<Guid> TopicIds,
    IReadOnlyList<SeriesAssignmentRequest> Series,
    IReadOnlyList<string> Tags) : IRequest;
