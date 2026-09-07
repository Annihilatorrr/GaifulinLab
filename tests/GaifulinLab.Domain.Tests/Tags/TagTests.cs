using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Common;

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

    [Fact]
    public void Create_RejectsNameThatExceedsPersistedContentLimit()
    {
        Assert.Throws<ArgumentException>(() => Tag.Create(new string('t', ContentLimits.TagName + 1)));
    }

    [Fact]
    public void Create_AcceptsNameAtPersistedContentLimit()
    {
        var tag = Tag.Create(new string('t', ContentLimits.TagName));

        Assert.Equal(ContentLimits.TagName, tag.Name.Length);
        Assert.Equal(ContentLimits.TagName, tag.NormalizedName.Length);
    }
}
