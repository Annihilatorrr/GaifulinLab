namespace GaifulinLab.Web.Features;

public static class SignInLinkVisibility
{
    public static bool ShouldShow(bool signInLinkEnabled, bool isLoggedIn) =>
        signInLinkEnabled && !isLoggedIn;
}
