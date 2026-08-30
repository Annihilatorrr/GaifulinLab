using GaifulinLab.Contracts.Media;
using MediatR;

namespace GaifulinLab.Application.Media.UploadMedia;

public sealed record UploadMediaCommand(
    Stream Content,
    string OriginalFileName,
    string ContentType,
    long Size) : IRequest<UploadMediaResponse>;
