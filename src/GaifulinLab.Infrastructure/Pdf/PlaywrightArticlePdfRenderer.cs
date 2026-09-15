using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GaifulinLab.Application.Common;
using GaifulinLab.Application.Content;
using GaifulinLab.Application.Media;
using GaifulinLab.Application.Pdf;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GaifulinLab.Infrastructure.Pdf;

internal sealed class PlaywrightArticlePdfRenderer(
    IArticleHtmlSanitizer htmlSanitizer,
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

    private readonly SemaphoreSlim _renderSlots = new(settings.MaximumConcurrentRenders);
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<byte[]> RenderAsync(
        ArticlePdfDocument document,
        ArticleTypography typography,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);

        var renderSlotAcquired = false;
        try
        {
            await _renderSlots.WaitAsync(timeout.Token);
            renderSlotAcquired = true;

            var html = await BuildHtmlAsync(document, typography, timeout.Token);
            var browser = await GetBrowserAsync(timeout.Token);

            await using var context = await browser.NewContextAsync().WaitAsync(timeout.Token);
            var pdf = await AwaitWithContextCancellationAsync(
                RenderInContextAsync(context, html),
                () => context.CloseAsync(),
                timeout.Token);

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
            await ResetBrowserAsync();
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
            if (renderSlotAcquired)
            {
                _renderSlots.Release();
            }
        }
    }

    private async Task<byte[]> RenderInContextAsync(IBrowserContext context, string html)
    {
        context.SetDefaultTimeout((float)settings.Timeout.TotalMilliseconds);

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

        var highlightJsPath = Path.GetFullPath(settings.HighlightJsPath);
        if (!File.Exists(highlightJsPath))
        {
            throw new PdfRenderingException(
                $"The local code highlighting asset was not found at '{settings.HighlightJsPath}'.");
        }

        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Content = await File.ReadAllTextAsync(highlightJsPath)
        });
        await page.EvaluateAsync("""
            () => {
                document.querySelectorAll("pre code[class*='language-']").forEach(code => {
                    try {
                        window.hljs?.highlightElement(code);
                    } catch {
                        // Unknown language labels deliberately fall back to plain source text.
                    }
                });
            }
            """);

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

        return await page.PdfAsync(new PagePdfOptions
        {
            Format = "A4",
            PrintBackground = true,
            PreferCSSPageSize = true
        });
    }

    internal static async Task<T> AwaitWithContextCancellationAsync<T>(
        Task<T> renderTask,
        Func<Task> closeContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await renderTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await closeContext();
            }
            catch (PlaywrightException)
            {
                // A disconnected Chromium cannot acknowledge context closure.
            }

            throw;
        }
    }

    private async Task<string> BuildHtmlAsync(
        ArticlePdfDocument document,
        ArticleTypography typography,
        CancellationToken cancellationToken)
    {
        var articleHtml = htmlSanitizer.RenderForDisplay(document.Html);
        articleHtml = ForceDisplayIntegralLimits(articleHtml);
        articleHtml = await EmbedInternalMediaAsync(articleHtml, cancellationToken);
        var articleStylesPath = Path.GetFullPath(settings.ArticleStylesPath);
        if (!File.Exists(articleStylesPath))
        {
            throw new PdfRenderingException(
                $"The shared article stylesheet was not found at '{settings.ArticleStylesPath}'.");
        }

        var articleStyles = await File.ReadAllTextAsync(articleStylesPath, cancellationToken);
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
                    :root {
                        color-scheme: light;
                        --color-surface: #fff;
                        --color-surface-subtle: #f5f7fb;
                        --color-surface-accent: #f2f5fb;
                        --color-text: #071735;
                        --color-text-muted: #5d6e92;
                        --color-border: #d6dfef;
                        --color-accent: #2647dd;
                        --color-danger: #c83232;
                        --color-code-keyword: #6d28d9;
                        --color-code-string: #a23a00;
                        --color-code-number: #08745b;
                        --color-code-comment: #65748b;
                        --font-mono: "Courier New", monospace;
                        --text-sm: 9pt;
                        --text-xl: 15pt;
                        --text-3xl: 26pt;
                        --line-heading: 1.2;
                        --radius-sm: 4px;
                        --radius-md: 5px;
                        --space-2: .45rem;
                        --space-3: .55rem;
                        --space-4: .75rem;
                        --space-5: .9rem;
                        --article-line-height: __LINE_HEIGHT__;
                        --article-block-spacing: __BLOCK_SPACING__rem;
                    }
                    * { box-sizing: border-box; }
                    body { margin: 0; color: #071735; background: #fff; font-family: Arial, Helvetica, sans-serif; font-size: 11pt; line-height: __LINE_HEIGHT__; }
                    .article-header { margin-bottom: 1.5rem; }
                    .article-header time { color: #5d6e92; font-size: 9pt; }
                    .article-header h1 { margin: .4rem 0 .7rem; font-size: 26pt; line-height: 1.1; }
                    .summary { color: #40547c; font-size: 13pt; }
                    .article-content img { max-height: 235mm; }
                    __ARTICLE_STYLES__
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
                <main>
                    <header class="article-header">__PUBLISHED_AT__<h1>__TITLE__</h1>__SUMMARY__</header>
                    <article class="article-content article-body">__ARTICLE_HTML__</article>
                </main>
            </body>
            </html>
            """;
        return template
            .Replace("__LANGUAGE__", WebUtility.HtmlEncode(document.LanguageCode), StringComparison.Ordinal)
            .Replace("__LINE_HEIGHT__", typography.LineHeight.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__BLOCK_SPACING__", typography.BlockSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__ARTICLE_STYLES__", articleStyles, StringComparison.Ordinal)
            .Replace("__PUBLISHED_AT__", publishedAt, StringComparison.Ordinal)
            .Replace("__TITLE__", title, StringComparison.Ordinal)
            .Replace("__SUMMARY__", summary, StringComparison.Ordinal)
            .Replace("__ARTICLE_HTML__", articleHtml, StringComparison.Ordinal);
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
    string MathJaxAssetsPath,
    string ArticleStylesPath,
    string HighlightJsPath);
