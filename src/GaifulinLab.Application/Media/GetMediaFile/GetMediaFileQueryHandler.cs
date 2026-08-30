using GaifulinLab.Application.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Media.GetMediaFile;

internal sealed class GetMediaFileQueryHandler(
    IAppDbContext dbContext,
    IMediaStorage storage) : IRequestHandler<GetMediaFileQuery, MediaFileResult?>
{
    public async Task<MediaFileResult?> Handle(
        GetMediaFileQuery request,
        CancellationToken cancellationToken)
    {
        var asset = await dbContext.MediaAssets
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.MediaId, cancellationToken);
        if (asset is null)
        {
            return null;
        }

        var content = await storage.OpenReadAsync(asset.RelativePath, cancellationToken);
        return content is null
            ? null
            : new MediaFileResult(content, asset.ContentType, asset.CreatedAt);
    }
}
