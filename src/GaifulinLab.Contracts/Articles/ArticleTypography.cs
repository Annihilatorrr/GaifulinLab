namespace GaifulinLab.Contracts.Articles;

/// <summary>
/// Personal reading-density preferences. They are deliberately not part of an article:
/// the browser stores them locally and a PDF request sends the active values explicitly.
/// </summary>
public sealed record ArticleTypography(decimal LineHeight, decimal BlockSpacing)
{
    public const decimal MinimumLineHeight = 1.00m;
    public const decimal MaximumLineHeight = 2.20m;
    public const decimal MinimumBlockSpacing = 0.10m;
    public const decimal MaximumBlockSpacing = 1.60m;

    public static ArticleTypography Default { get; } = new(1.75m, 0.75m);

    public static ArticleTypography FromOptional(decimal? lineHeight, decimal? blockSpacing) =>
        new(
            Clamp(lineHeight ?? Default.LineHeight, MinimumLineHeight, MaximumLineHeight),
            Clamp(blockSpacing ?? Default.BlockSpacing, MinimumBlockSpacing, MaximumBlockSpacing));

    private static decimal Clamp(decimal value, decimal minimum, decimal maximum) =>
        Math.Clamp(value, minimum, maximum);
}
