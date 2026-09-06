namespace GaifulinLab.Web.Features;

public static class RegistrationLinkVisibility
{
    public static bool ShouldShow(bool registrationLinkEnabled, bool isLoggedIn) =>
        registrationLinkEnabled && !isLoggedIn;
}
