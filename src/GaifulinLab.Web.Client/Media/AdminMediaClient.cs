using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Media;

namespace GaifulinLab.Web.Client.Media;

public sealed class AdminMediaClient(HttpClient httpClient)
{
    public async Task<UploadMediaResponse> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var multipart = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            fileContent.Headers.ContentType = mediaType;
        }
        multipart.Add(fileContent, "file", fileName);

        var response = await httpClient.PostAsync("/api/admin/media", multipart, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
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

            throw new MediaUploadException(error?.Message ?? "The image could not be uploaded.");
        }

        return await response.Content.ReadFromJsonAsync<UploadMediaResponse>(cancellationToken)
            ?? throw new MediaUploadException("The server returned an empty upload response.");
    }
}

public sealed class MediaUploadException(string message) : Exception(message);
