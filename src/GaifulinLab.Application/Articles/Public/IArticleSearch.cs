using GaifulinLab.Contracts.Articles;

namespace GaifulinLab.Application.Articles.Public;

public interface IArticleSearch
{
    Task<ArticleSearchResponse> SearchAsync(ArticleSearchRequest request, CancellationToken cancellationToken);
}
