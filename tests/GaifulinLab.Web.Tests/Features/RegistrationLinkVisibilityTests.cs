using GaifulinLab.Web.Features;

namespace GaifulinLab.Web.Tests.Features;

public sealed class RegistrationLinkVisibilityTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ShouldShow_RequiresEnabledRegistrationAndAnonymousUser(
        bool registrationLinkEnabled,
        bool isLoggedIn,
        bool expected)
    {
        var visible = RegistrationLinkVisibility.ShouldShow(registrationLinkEnabled, isLoggedIn);

        Assert.Equal(expected, visible);
    }
}
