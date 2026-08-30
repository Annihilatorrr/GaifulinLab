using System.Text;

namespace GaifulinLab.Domain.Common;

internal static class DomainRules
{
    public static string NormalizeLanguageCode(string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);

        var normalized = languageCode.Trim().ToLowerInvariant();
        if (normalized.Length != 2 || normalized.Any(character => character is < 'a' or > 'z'))
        {
            throw new ArgumentException("Language code must contain two ASCII letters.", nameof(languageCode));
        }

        return normalized;
    }

    public static string? NormalizeOptionalSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return NormalizeSlug(slug);
    }

    public static string NormalizeSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var builder = new StringBuilder(slug.Length);
        var separatorPending = false;

        foreach (var character in slug.Trim().ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separatorPending && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(character);
                separatorPending = false;
                continue;
            }

            if (character == '-' || character == '_' || char.IsWhiteSpace(character))
            {
                separatorPending = builder.Length > 0;
                continue;
            }

            throw new ArgumentException("Slug may contain only ASCII letters, digits and hyphens.", nameof(slug));
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException("Slug must contain at least one letter or digit.", nameof(slug));
        }

        return builder.ToString();
    }

    public static string RequireTrimmed(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    public static DateTimeOffset AsUtc(DateTimeOffset value) => value.ToUniversalTime();
}
