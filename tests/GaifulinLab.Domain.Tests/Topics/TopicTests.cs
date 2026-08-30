using GaifulinLab.Domain.Topics;

namespace GaifulinLab.Domain.Tests.Topics;

public sealed class TopicTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddLocalization_RejectsDuplicateLanguage()
    {
        var topic = Topic.Create("en", "Digital Signal Processing", "dsp", null, CreatedAt);

        Assert.Throws<InvalidOperationException>(
            () => topic.AddLocalization("EN", "DSP", "dsp", null, CreatedAt));
    }
}
