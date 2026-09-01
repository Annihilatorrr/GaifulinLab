using GaifulinLab.Web.Components.Admin;

namespace GaifulinLab.Web.Tests.Admin;

public sealed class SlugSuggestionTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Test Markdown → PDF", "test-markdown-pdf")]
    [InlineData("Почему дисперсию делят на n-1", "pochemu-dispersiyu-delyat-na-n-1")]
    [InlineData("Ёжик, чай и щука", "yozhik-chay-i-shchuka")]
    [InlineData("Мягкий знак и подъезд", "myagkiy-znak-i-podezd")]
    [InlineData("Café déjà vu", "cafe-deja-vu")]
    public void FromTitle_ReturnsExpectedSlug(string? title, string expected)
    {
        Assert.Equal(expected, SlugSuggestion.FromTitle(title));
    }

    [Fact]
    public void FromTitle_LimitsSuggestionToDatabaseColumnLength()
    {
        var title = string.Join(' ', Enumerable.Repeat("article", 100));

        var slug = SlugSuggestion.FromTitle(title);

        Assert.InRange(slug.Length, 1, 200);
        Assert.False(slug.EndsWith("-", StringComparison.Ordinal));
    }
}
