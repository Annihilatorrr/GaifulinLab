using GaifulinLab.Contracts.Articles;

namespace GaifulinLab.Application.Pdf;

public interface IArticlePdfRenderer
{
    Task<byte[]> RenderAsync(
        string languageCode,
        string slug,
        ArticleTypography typography,
        CancellationToken cancellationToken);
}
