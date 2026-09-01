using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GaifulinLab.Application.Common;
using GaifulinLab.Application.Content;
using GaifulinLab.Application.Media;
using GaifulinLab.Application.Pdf;
using GaifulinLab.Contracts;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GaifulinLab.Infrastructure.Pdf;

internal sealed class PlaywrightArticlePdfRenderer(
    IMarkdownRenderer markdownRenderer,
    IMediaStorage mediaStorage,
    IServiceScopeFactory scopeFactory,
    ArticlePdfRendererSettings settings,
    ILogger<PlaywrightArticlePdfRenderer> logger) : IArticlePdfRenderer, IAsyncDisposable
{
    private const int MaximumPdfSizeBytes = 50 * 1024 * 1024;
    private const long MaximumEmbeddedMediaBytes = 10 * 1024 * 1024;
    private static readonly Regex MediaSource = new(
        "src=\\\"/media/(?<id>[0-9a-fA-F-]{36})\\\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DisplayMath = new(
        "<div class=\\\"math\\\">(?<formula>.*?)</div>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex IntegralWithoutExplicitLimits = new(
        @"\\int(?!\\(?:limits|nolimits))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string PdfStylesheet = LoadPdfStylesheet();

    private readonly SemaphoreSlim _renderSlots = new(settings.MaximumConcurrentRenders);
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<byte[]> RenderAsync(
        ArticlePdfDocument document,
        ArticleTypography typography,
        CancellationToken cancellationToken)
    {
        await _renderSlots.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.Timeout);
            var token = timeout.Token;
            var html = await BuildHtmlAsync(document, typography, token);
            var browser = await GetBrowserAsync(token);

            await using var context = await browser.NewContextAsync();
            var mathJaxAssetsPath = Path.GetFullPath(settings.MathJaxAssetsPath);
            if (!Directory.Exists(mathJaxAssetsPath))
            {
                throw new PdfRenderingException(
                    $"The local MathJax assets were not found at '{settings.MathJaxAssetsPath}'.");
            }

            var mathJaxAssetsRoot = mathJaxAssetsPath.EndsWith(Path.DirectorySeparatorChar)
                ? mathJaxAssetsPath
                : $"{mathJaxAssetsPath}{Path.DirectorySeparatorChar}";
            var mathJaxPath = Path.GetFullPath(settings.MathJaxPath);
            if (!File.Exists(mathJaxPath))
            {
                throw new PdfRenderingException(
                    $"The local MathJax asset was not found at '{settings.MathJaxPath}'.");
            }

            if (!mathJaxPath.StartsWith(mathJaxAssetsRoot, StringComparison.Ordinal))
            {
                throw new PdfRenderingException(
                    $"The local MathJax asset must be located under '{settings.MathJaxAssetsPath}'.");
            }

            var mathJaxScriptUrl = $"https://mathjax.local/{Path.GetRelativePath(mathJaxAssetsPath, mathJaxPath).Replace(Path.DirectorySeparatorChar, '/')}";
            await context.RouteAsync("**/*", async route =>
            {
                var uri = new Uri(route.Request.Url);
                if (string.Equals(uri.Host, "mathjax.local", StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = uri.AbsolutePath.TrimStart('/');
                    var assetPath = Path.GetFullPath(Path.Combine(
                        mathJaxAssetsPath,
                        relativePath.Replace('/', Path.DirectorySeparatorChar)));

                    if (!assetPath.StartsWith(mathJaxAssetsRoot, StringComparison.Ordinal)
                        || !File.Exists(assetPath))
                    {
                        await route.AbortAsync();
                        return;
                    }

                    await route.FulfillAsync(new RouteFulfillOptions { Path = assetPath });
                    return;
                }

                if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                {
                    await route.AbortAsync();
                    return;
                }

                await route.ContinueAsync();
            });
            var page = await context.NewPageAsync();
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Microsoft.Playwright.Media.Print });
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });

            await page.AddScriptTagAsync(new PageAddScriptTagOptions
            {
                Url = mathJaxScriptUrl
            });
            await page.EvaluateAsync("""
                async () => {
                    await document.fonts.ready;
                    if (!window.MathJax?.startup?.promise) {
                        throw new Error('MathJax did not initialize.');
                    }

                    if (typeof window.MathJax.typesetPromise !== 'function') {
                        window.MathJax.startup.defaultReady();
                    }

                    await window.MathJax.startup.promise;
                    if (typeof window.MathJax.typesetPromise !== 'function') {
                        throw new Error('MathJax typesetting API did not initialize.');
                    }

                    await window.MathJax.typesetPromise();
                    await document.fonts.ready;
                    document.documentElement.dataset.pdfReady = 'true';
                }
                """);

            var pdf = await page.PdfAsync(new PagePdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                PreferCSSPageSize = true
            });

            if (pdf.Length < 5 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            {
                throw new PdfRenderingException("The PDF renderer returned an invalid document.");
            }

            if (pdf.Length > MaximumPdfSizeBytes)
            {
                throw new PdfRenderingException("The generated article PDF is too large.");
            }

            return pdf;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PdfRenderingException("The article PDF renderer timed out.");
        }
        catch (PlaywrightException exception)
        {
            logger.LogWarning(exception, "Chromium failed while rendering article PDF {LanguageCode}/{Slug}.", document.LanguageCode, document.Slug);
            await ResetBrowserAsync();
            throw new PdfRenderingException("The article PDF renderer is unavailable.", exception);
        }
        finally
        {
            _renderSlots.Release();
        }
    }

    private async Task<string> BuildHtmlAsync(
        ArticlePdfDocument document,
        ArticleTypography typography,
        CancellationToken cancellationToken)
    {
        var articleHtml = markdownRenderer.Render(document.Markdown);
        articleHtml = ForceDisplayIntegralLimits(articleHtml);
        articleHtml = await EmbedInternalMediaAsync(articleHtml, cancellationToken);
        var title = WebUtility.HtmlEncode(document.Title);
        var summary = string.IsNullOrWhiteSpace(document.Summary)
            ? string.Empty
            : $"<p class=\"summary\">{WebUtility.HtmlEncode(document.Summary)}</p>";
        var publishedAt = document.PublishedAt is { } date
            ? $"<time>{WebUtility.HtmlEncode(date.ToLocalTime().ToString("d MMM yyyy"))}</time>"
            : string.Empty;

        var template = """
            <!doctype html>
            <html lang="__LANGUAGE__">
            <head>
                <meta charset="utf-8">
                <style>
                    @page { size: A4; margin: 18mm 16mm; }
                    :root { color-scheme: light; }
                    * { box-sizing: border-box; }
                    body { margin: 0; background: #fff; }
                    __PDF_STYLESHEET__
                </style>
                <script>
                    window.MathJax = {
                        startup: { typeset: false },
                        output: {
                            font: 'mathjax-newcm',
                            fontPath: 'https://mathjax.local/%%FONT%%-font'
                        }
                    };
                </script>
            </head>
            <body>
                <main class="pdf-article-document" style="--article-line-height: __LINE_HEIGHT__; --article-block-spacing: __BLOCK_SPACING__rem;">
                    <header class="article-header">__PUBLISHED_AT__<h1>__TITLE__</h1>__SUMMARY__</header>
                    <article class="article-body">__ARTICLE_HTML__</article>
                </main>
            </body>
            </html>
            """;
        return template
            .Replace("__LANGUAGE__", WebUtility.HtmlEncode(document.LanguageCode), StringComparison.Ordinal)
            .Replace("__LINE_HEIGHT__", typography.LineHeight.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__BLOCK_SPACING__", typography.BlockSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__PDF_STYLESHEET__", PdfStylesheet, StringComparison.Ordinal)
            .Replace("__PUBLISHED_AT__", publishedAt, StringComparison.Ordinal)
            .Replace("__TITLE__", title, StringComparison.Ordinal)
            .Replace("__SUMMARY__", summary, StringComparison.Ordinal)
            .Replace("__ARTICLE_HTML__", articleHtml, StringComparison.Ordinal);
    }

    private static string LoadPdfStylesheet()
    {
        const string resourceName = "GaifulinLab.Contracts.Content.article-pdf.css";
        using var stream = typeof(ContractsAssembly).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task<string> EmbedInternalMediaAsync(string html, CancellationToken cancellationToken)
    {
        var mediaIds = MediaSource.Matches(html)
            .Select(match => Guid.TryParse(match.Groups["id"].Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (mediaIds.Length == 0)
        {
            return html;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assets = await dbContext.MediaAssets
            .AsNoTracking()
            .Where(asset => mediaIds.Contains(asset.Id))
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);
        var replacements = new Dictionary<Guid, string>();

        foreach (var mediaId in mediaIds)
        {
            if (!assets.TryGetValue(mediaId, out var asset) || asset.Size > MaximumEmbeddedMediaBytes)
            {
                continue;
            }

            await using var content = await mediaStorage.OpenReadAsync(asset.RelativePath, cancellationToken);
            if (content is null)
            {
                continue;
            }

            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            replacements[mediaId] = $"src=\"data:{asset.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}\"";
        }

        return MediaSource.Replace(html, match =>
        {
            var mediaId = Guid.Parse(match.Groups["id"].Value);
            return replacements.TryGetValue(mediaId, out var replacement) ? replacement : match.Value;
        });
    }

    internal static string ForceDisplayIntegralLimits(string html) =>
        DisplayMath.Replace(html, match =>
        {
            var formula = match.Groups["formula"].Value;
            var normalizedFormula = IntegralWithoutExplicitLimits.Replace(formula, @"\int\limits");
            return match.Value.Replace(formula, normalizedFormula, StringComparison.Ordinal);
        });

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true })
        {
            return _browser;
        }

        await _browserLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser is { IsConnected: true })
            {
                return _browser;
            }

            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--disable-dev-shm-usage", "--no-sandbox"]
            });
            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    private async Task ResetBrowserAsync()
    {
        await _browserLock.WaitAsync();
        try
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
                _browser = null;
            }
        }
        finally
        {
            _browserLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetBrowserAsync();
        _playwright?.Dispose();
        _renderSlots.Dispose();
        _browserLock.Dispose();
    }
}

internal sealed record ArticlePdfRendererSettings(
    TimeSpan Timeout,
    int MaximumConcurrentRenders,
    string MathJaxPath,
    string MathJaxAssetsPath);
