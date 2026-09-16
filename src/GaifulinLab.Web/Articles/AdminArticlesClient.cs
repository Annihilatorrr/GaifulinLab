using System.Net.Http.Json;
using System.Text.Json;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Taxonomy;

namespace GaifulinLab.Web.Articles;

public sealed class AdminArticlesClient(HttpClient httpClient)
{
    public Task<AdminArticleListResponse> GetArticlesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetAsync<AdminArticleListResponse>(
            $"/api/admin/articles?page={page}&pageSize={pageSize}",
            cancellationToken);

    public Task<AdminArticleDetailsDto> GetArticleAsync(
        Guid articleId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AdminArticleDetailsDto>($"/api/admin/articles/{articleId}", cancellationToken);

    public Task<AdminTaxonomyDto> GetTaxonomyAsync(CancellationToken cancellationToken = default) =>
        GetAsync<AdminTaxonomyDto>("/api/admin/taxonomy", cancellationToken);

    public async Task<AdminTopicDto> CreateTopicAsync(
        CreateTopicRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/admin/taxonomy/topics", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdminTopicDto>(cancellationToken)
            ?? throw new AdminApiException("The server returned an empty response.");
    }

    public async Task<AdminSeriesDto> CreateSeriesAsync(
        CreateSeriesRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/admin/taxonomy/series", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdminSeriesDto>(cancellationToken)
            ?? throw new AdminApiException("The server returned an empty response.");
    }

    public async Task<AdminTopicDto> AddTopicLocalizationAsync(
        Guid topicId,
        CreateTopicRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/admin/taxonomy/topics/{topicId}/localizations",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdminTopicDto>(cancellationToken)
            ?? throw new AdminApiException("The server returned an empty response.");
    }

    public async Task<AdminSeriesDto> AddSeriesLocalizationAsync(
        Guid seriesId,
        CreateSeriesRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/admin/taxonomy/series/{seriesId}/localizations",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdminSeriesDto>(cancellationToken)
            ?? throw new AdminApiException("The server returned an empty response.");
    }

    public async Task<AdminTopicDto> UpdateTopicLocalizationAsync(
        Guid topicId,
        string languageCode,
        UpdateTopicLocalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/admin/taxonomy/topics/{topicId}/localizations/{Uri.EscapeDataString(languageCode)}",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdminTopicDto>(cancellationToken)
            ?? throw new AdminApiException("The server returned an empty response.");
    }

    public async Task<AdminSeriesDto> UpdateSeriesLocalizationAsync(
        Guid seriesId,
        string languageCode,
        UpdateSeriesLocalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/admin/taxonomy/series/{seriesId}/localizations/{Uri.EscapeDataString(languageCode)}",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AdminSeriesDto>(cancellationToken)
            ?? throw new AdminApiException("The server returned an empty response.");
    }

    public async Task<CreateArticleResponse> CreateArticleAsync(
        CreateArticleRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/admin/articles", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CreateArticleResponse>(cancellationToken)
            ?? throw new AdminApiException("The server returned an empty response.");
    }

    public async Task<long> UpdateLocalizationAsync(
        Guid articleId,
        string languageCode,
        UpdateArticleLocalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/admin/articles/{articleId}/localizations/{Uri.EscapeDataString(languageCode)}",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<long>(cancellationToken);
    }

    public Task PublishAsync(Guid articleId, string languageCode, CancellationToken cancellationToken = default) =>
        PostAsync($"/api/admin/articles/{articleId}/localizations/{Uri.EscapeDataString(languageCode)}/publish", cancellationToken);

    public Task UnpublishAsync(Guid articleId, string languageCode, CancellationToken cancellationToken = default) =>
        PostAsync($"/api/admin/articles/{articleId}/localizations/{Uri.EscapeDataString(languageCode)}/unpublish", cancellationToken);

    public async Task UpdateTaxonomyAsync(
        Guid articleId,
        UpdateArticleTaxonomyRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/admin/articles/{articleId}/taxonomy",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/api/admin/articles/{articleId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new AdminApiException("The server returned an empty response.");
    }

    private async Task PostAsync(string uri, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync(uri, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken);
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        throw new AdminApiException(
            error?.Message ?? "The request could not be completed.",
            error?.Code);
    }
}

public sealed class AdminApiException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
