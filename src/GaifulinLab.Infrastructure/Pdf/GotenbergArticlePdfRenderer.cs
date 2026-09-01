using GaifulinLab.Application.Common;
using GaifulinLab.Application.Pdf;
using GaifulinLab.Contracts.Articles;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace GaifulinLab.Infrastructure.Pdf;

internal sealed class GotenbergArticlePdfRenderer(
    IHttpClientFactory httpClientFactory,
    ArticlePdfRendererSettings settings,
    ILogger<GotenbergArticlePdfRenderer> logger) : IArticlePdfRenderer
{
    internal const string HttpClientName = "article-pdf-renderer";
    private const int MaximumPdfSizeBytes = 50 * 1024 * 1024;
    private const string ReadyExpression =
        "document.querySelector('[data-pdf-ready=\"true\"]') !== null" +
        " && document.fonts.status === 'loaded'" +
        " && Array.from(document.images).every(image => image.complete && image.naturalWidth > 0)";

    private readonly SemaphoreSlim _renderSlots = new(settings.MaximumConcurrentRenders);

    public async Task<byte[]> RenderAsync(
        string languageCode,
        string slug,
        ArticleTypography typography,
        CancellationToken cancellationToken)
    {
        await _renderSlots.WaitAsync(cancellationToken);
        try
        {
            return await RenderCoreAsync(languageCode, slug, typography, cancellationToken);
        }
        finally
        {
            _renderSlots.Release();
        }
    }

    private async Task<byte[]> RenderCoreAsync(
        string languageCode,
        string slug,
        ArticleTypography typography,
        CancellationToken cancellationToken)
    {
        var articleUri = new Uri(
            settings.ArticleBaseUri,
            $"{Uri.EscapeDataString(languageCode)}/articles/{Uri.EscapeDataString(slug)}?pdf=1&lineHeight={typography.LineHeight.ToString(CultureInfo.InvariantCulture)}&blockSpacing={typography.BlockSpacing.ToString(CultureInfo.InvariantCulture)}");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "forms/chromium/convert/url");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(articleUri.AbsoluteUri), "url" },
            { new StringContent("true"), "printBackground" },
            { new StringContent("true"), "preferCssPageSize" },
            { new StringContent("print"), "emulatedMediaType" },
            { new StringContent(ReadyExpression), "waitForExpression" },
            { new StringContent("true"), "generateDocumentOutline" },
            { new StringContent("true"), "generateTaggedPdf" }
        };
        request.Content = form;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var details = await ReadLimitedErrorAsync(response.Content, cancellationToken);
                logger.LogWarning(
                    "Gotenberg rejected PDF rendering for {ArticleUri} with status {StatusCode}: {Details}",
                    articleUri,
                    (int)response.StatusCode,
                    details);
                throw new PdfRenderingException("The article PDF could not be generated.");
            }

            if (response.Content.Headers.ContentLength is > MaximumPdfSizeBytes)
            {
                throw new PdfRenderingException("The generated article PDF is too large.");
            }

            var pdf = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (pdf.Length > MaximumPdfSizeBytes)
            {
                throw new PdfRenderingException("The generated article PDF is too large.");
            }

            if (pdf.Length < 5 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            {
                throw new PdfRenderingException("The PDF renderer returned an invalid document.");
            }

            return pdf;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PdfRenderingException("The article PDF renderer timed out.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The article PDF renderer is unavailable.");
            throw new PdfRenderingException("The article PDF renderer is unavailable.", exception);
        }
    }

    private static async Task<string> ReadLimitedErrorAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        const int maximumLength = 2_000;
        var details = await content.ReadAsStringAsync(cancellationToken);
        return details.Length <= maximumLength ? details : details[..maximumLength];
    }
}

internal sealed record ArticlePdfRendererSettings(
    Uri ArticleBaseUri,
    int MaximumConcurrentRenders);
