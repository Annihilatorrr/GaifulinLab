namespace GaifulinLab.Application.Content;

/// <summary>
/// Converts an author-supplied article HTML fragment into the safe fragment that
/// may be persisted and later rendered by a presentation layer.
/// </summary>
public interface IArticleHtmlSanitizer
{
    string Sanitize(string html);

    /// <summary>
    /// Produces a safe transient article fragment for a reader-facing surface.
    /// The result may include display-only generated markup and must not be persisted.
    /// </summary>
    string RenderForDisplay(string html);
}
