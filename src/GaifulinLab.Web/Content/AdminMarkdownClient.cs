using System.Net.Http.Json;
using GaifulinLab.Contracts.Content;

namespace GaifulinLab.Web.Content;

public sealed class AdminMarkdownClient(HttpClient httpClient)
{
    public async Task<string> PreviewAsync(string markdown, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/admin/markdown/preview",
            new MarkdownPreviewRequest(markdown),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var preview = await response.Content.ReadFromJsonAsync<MarkdownPreviewResponse>(cancellationToken);
        return preview?.Html ?? string.Empty;
    }
}
