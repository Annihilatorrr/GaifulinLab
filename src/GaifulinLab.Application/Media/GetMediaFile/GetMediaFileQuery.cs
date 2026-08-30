using MediatR;

namespace GaifulinLab.Application.Media.GetMediaFile;

public sealed record GetMediaFileQuery(Guid MediaId) : IRequest<MediaFileResult?>;

public sealed record MediaFileResult(
    Stream Content,
    string ContentType,
    DateTimeOffset CreatedAt);
