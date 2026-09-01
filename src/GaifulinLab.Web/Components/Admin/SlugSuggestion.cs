using System.Globalization;
using System.Text;

namespace GaifulinLab.Web.Components.Admin;

internal static class SlugSuggestion
{
    private const int MaximumLength = 200;

    public static string FromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        var separatorPending = false;

        foreach (var sourceCharacter in title.Trim())
        {
            var character = char.ToLowerInvariant(sourceCharacter);
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                AppendToken(builder, character.ToString(), ref separatorPending);
                continue;
            }

            var transliteration = TransliterateCyrillic(character);
            if (transliteration is not null)
            {
                AppendToken(builder, transliteration, ref separatorPending);
                continue;
            }

            var decomposed = character.ToString().Normalize(NormalizationForm.FormD);
            var latinBase = decomposed.FirstOrDefault(candidate =>
                candidate is >= 'a' and <= 'z');
            if (latinBase != default)
            {
                AppendToken(builder, latinBase.ToString(), ref separatorPending);
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                separatorPending = builder.Length > 0;
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    private static void AppendToken(
        StringBuilder builder,
        string token,
        ref bool separatorPending)
    {
        if (builder.Length >= MaximumLength)
        {
            return;
        }

        if (separatorPending && builder.Length > 0 && builder.Length < MaximumLength)
        {
            builder.Append('-');
        }

        var remainingLength = MaximumLength - builder.Length;
        builder.Append(token.AsSpan(0, Math.Min(token.Length, remainingLength)));
        separatorPending = false;
    }

    private static string? TransliterateCyrillic(char character) => character switch
    {
        'а' => "a",
        'б' => "b",
        'в' => "v",
        'г' => "g",
        'д' => "d",
        'е' => "e",
        'ё' => "yo",
        'ж' => "zh",
        'з' => "z",
        'и' => "i",
        'й' => "y",
        'к' => "k",
        'л' => "l",
        'м' => "m",
        'н' => "n",
        'о' => "o",
        'п' => "p",
        'р' => "r",
        'с' => "s",
        'т' => "t",
        'у' => "u",
        'ф' => "f",
        'х' => "kh",
        'ц' => "ts",
        'ч' => "ch",
        'ш' => "sh",
        'щ' => "shch",
        'ъ' => string.Empty,
        'ы' => "y",
        'ь' => string.Empty,
        'э' => "e",
        'ю' => "yu",
        'я' => "ya",
        _ => null
    };
}
