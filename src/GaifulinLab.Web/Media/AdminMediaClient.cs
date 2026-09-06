using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Media;

namespace GaifulinLab.Web.Media;

// Article editor client for uploading images to the protected admin API.
public sealed class AdminMediaClient(HttpClient httpClient)
{
    // Sends a file as multipart/form-data to POST /api/admin/media.
    // The calling code owns the stream, but it is disposed together with StreamContent
    // when this method completes, so it cannot be used after UploadAsync.
    public async Task<UploadMediaResponse> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        // MultipartFormDataContent corresponds to a regular HTML form with a type="file" field.
        using var multipart = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);

        // The API uses Content-Type to verify that the uploaded image type is allowed.
        // If the browser supplied an invalid value, leave the header unset so that
        // the server returns a clear error instead of the client failing.
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            fileContent.Headers.ContentType = mediaType;
        }
        multipart.Add(fileContent, "file", fileName);

        // AdminAuthorizationHandler, registered for this HttpClient, adds the authorization header.
        var response = await httpClient.PostAsync("/api/admin/media", multipart, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ApiErrorResponse? error = null;
            try
            {
                // The API normally returns JSON with an error message that can be shown to the article author.
                error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken);
            }
            catch (JsonException)
            {
                // For example, a proxy may have returned HTML instead of the expected JSON.
            }
            catch (NotSupportedException)
            {
                // A response without a JSON Content-Type can still fall back to a generic message.
            }

            // The UI catches this exception and renders the message next to the upload form.
            throw new MediaUploadException(error?.Message ?? "The image could not be uploaded.");
        }

        // A successful response contains the media ID and URL that the editor inserts into Markdown.
        return await response.Content.ReadFromJsonAsync<UploadMediaResponse>(cancellationToken)
            ?? throw new MediaUploadException("The server returned an empty upload response.");
    }
}

// UI-level error: hides HTTP details while preserving a clear message for the user.
public sealed class MediaUploadException(string message) : Exception(message);
