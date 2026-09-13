namespace GaifulinLab.Application.Content;

/// <summary>
/// Converts an author-supplied article HTML fragment into the safe fragment that
/// may be persisted and later rendered by a presentation layer.
/// </summary>
public interface IArticleHtmlSanitizer
{
    string Sanitize(string html);
}
