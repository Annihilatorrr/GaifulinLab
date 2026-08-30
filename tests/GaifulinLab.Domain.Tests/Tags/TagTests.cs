using GaifulinLab.Domain.Tags;

namespace GaifulinLab.Domain.Tests.Tags;

public sealed class TagTests
{
    [Fact]
    public void Create_PreservesDisplayNameAndNormalizesLookupName()
    {
        var tag = Tag.Create(" .NET ");

        Assert.Equal(".NET", tag.Name);
        Assert.Equal(".net", tag.NormalizedName);
    }
}
