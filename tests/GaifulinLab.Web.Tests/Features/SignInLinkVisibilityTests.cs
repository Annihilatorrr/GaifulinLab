using GaifulinLab.Web.Features;

namespace GaifulinLab.Web.Tests.Features;

public sealed class SignInLinkVisibilityTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ShouldShow_RequiresEnabledFeatureAndAnonymousUser(
        bool signInLinkEnabled,
        bool isLoggedIn,
        bool expected)
    {
        var visible = SignInLinkVisibility.ShouldShow(signInLinkEnabled, isLoggedIn);

        Assert.Equal(expected, visible);
    }
}
