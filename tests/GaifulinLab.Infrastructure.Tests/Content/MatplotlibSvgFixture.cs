using System.IO.Compression;
using System.Text;

namespace GaifulinLab.Infrastructure.Tests.Content;

/// <summary>Exact SVG exported by Matplotlib and supplied with this change.</summary>
internal static class MatplotlibSvgFixture
{
    internal static string Get()
    {
        using var compressed = new MemoryStream(Convert.FromBase64String(Compressed));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var source = new StreamReader(gzip, Encoding.UTF8);
        return source.ReadToEnd();
    }

    private static readonly string Compressed = string.Concat(
        "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"
    );
}
