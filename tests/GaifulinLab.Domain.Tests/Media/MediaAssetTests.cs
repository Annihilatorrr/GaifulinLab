using GaifulinLab.Domain.Media;

namespace GaifulinLab.Domain.Tests.Media;

public sealed class MediaAssetTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StoresNormalizedMetadata()
    {
        var asset = MediaAsset.Create(
            " diagram.png ",
            "asset-id.png",
            "images/asset-id.png",
            "image/png",
            1_024,
            CreatedAt,
            800,
            600);

        Assert.Equal("diagram.png", asset.OriginalFileName);
        Assert.Equal("asset-id.png", asset.StoredFileName);
        Assert.Equal(1_024, asset.Size);
        Assert.Equal(800, asset.Width);
        Assert.Equal(600, asset.Height);
    }

    [Fact]
    public void Create_RejectsEmptyFile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaAsset.Create(
            "diagram.png",
            "asset-id.png",
            "images/asset-id.png",
            "image/png",
            0,
            CreatedAt));
    }
}
