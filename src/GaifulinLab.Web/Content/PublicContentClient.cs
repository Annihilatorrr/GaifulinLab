using System.Net;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Taxonomy;

namespace GaifulinLab.Web.Content;

public sealed class PublicContentClient(HttpClient httpClient, Uri pdfApiBaseAddress)
{
    public string AssetBaseUrl => httpClient.BaseAddress!.AbsoluteUri;
    public string PdfAssetBaseUrl => pdfApiBaseAddress.AbsoluteUri;

    public string GetArticlePdfUrl(
        string languageCode,
        string slug,
        ArticleTypography typography)
    {
        var relativeUri =
            $"api/public/articles/{Uri.EscapeDataString(languageCode)}/{Uri.EscapeDataString(slug)}/pdf" +
            $"?lineHeight={typography.LineHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&blockSpacing={typography.BlockSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return new Uri(httpClient.BaseAddress!, relativeUri).AbsoluteUri;
    }

    public Task<IReadOnlyList<PublicArticleListItemDto>> GetArticlesAsync(
        string languageCode,
        string? topic = null,
        string? series = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>
        {
            $"languageCode={Uri.EscapeDataString(languageCode)}"
        };
        Add(parameters, "topic", topic);
        Add(parameters, "series", series);
        Add(parameters, "tag", tag);
        return GetAsync<IReadOnlyList<PublicArticleListItemDto>>(
            $"/api/public/articles?{string.Join('&', parameters)}",
            cancellationToken);
    }

    public Task<PublicArticleDetailsDto?> GetArticleAsync(
        string languageCode,
        string slug,
        bool forPdf = false,
        CancellationToken cancellationToken = default) =>
        forPdf
            ? GetOptionalAsync<PublicArticleDetailsDto>(
                new Uri(
                    pdfApiBaseAddress,
                    $"api/public/articles/{Uri.EscapeDataString(languageCode)}/{Uri.EscapeDataString(slug)}"),
                cancellationToken)
            : GetOptionalAsync<PublicArticleDetailsDto>(
                $"/api/public/articles/{Uri.EscapeDataString(languageCode)}/{Uri.EscapeDataString(slug)}",
                cancellationToken);

    public Task<IReadOnlyList<PublicTopicDto>> GetTopicsAsync(
        string languageCode,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<PublicTopicDto>>(
            $"/api/public/topics/{Uri.EscapeDataString(languageCode)}",
            cancellationToken);

    public Task<IReadOnlyList<PublicSeriesListItemDto>> GetSeriesAsync(
        string languageCode,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<PublicSeriesListItemDto>>(
            $"/api/public/series/{Uri.EscapeDataString(languageCode)}",
            cancellationToken);

    public Task<PublicSeriesDetailsDto?> GetSeriesDetailsAsync(
        string languageCode,
        string slug,
        CancellationToken cancellationToken = default) =>
        GetOptionalAsync<PublicSeriesDetailsDto>(
            $"/api/public/series/{Uri.EscapeDataString(languageCode)}/{Uri.EscapeDataString(slug)}",
            cancellationToken);

    public Task<IReadOnlyList<PublicTagDto>> GetTagsAsync(
        string languageCode,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<PublicTagDto>>(
            $"/api/public/tags/{Uri.EscapeDataString(languageCode)}",
            cancellationToken);

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException("The server returned an empty response.");
    }

    private async Task<T?> GetOptionalAsync<T>(string uri, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(uri, cancellationToken);
        return await ReadOptionalAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(uri, cancellationToken);
        return await ReadOptionalAsync<T>(response, cancellationToken);
    }

    private static async Task<T?> ReadOptionalAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static void Add(ICollection<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }
}
