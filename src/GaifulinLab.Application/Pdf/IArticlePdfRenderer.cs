using GaifulinLab.Contracts.Articles;

namespace GaifulinLab.Application.Pdf;

public interface IArticlePdfRenderer
{
    Task<byte[]> RenderAsync(
        ArticlePdfDocument document,
        ArticleTypography typography,
        CancellationToken cancellationToken);
}

public sealed record ArticlePdfDocument(
    string LanguageCode,
    string Slug,
    string Title,
    string? Summary,
    string Markdown,
    DateTimeOffset? PublishedAt);
