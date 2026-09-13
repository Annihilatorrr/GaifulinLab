using System.Net.Http.Json;
using GaifulinLab.Contracts.Content;

namespace GaifulinLab.Web.Content;

public sealed class AdminHtmlClient(HttpClient httpClient)
{
    public async Task<string> PreviewAsync(string html, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/admin/html/preview",
            new HtmlPreviewRequest(html),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var preview = await response.Content.ReadFromJsonAsync<HtmlPreviewResponse>(cancellationToken);
        return preview?.Html ?? string.Empty;
    }
}
