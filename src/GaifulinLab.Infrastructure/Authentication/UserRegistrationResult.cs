namespace GaifulinLab.Infrastructure.Authentication;

public sealed record UserRegistrationResult(
    bool Succeeded,
    bool LoginTaken,
    IReadOnlyList<string> Errors)
{
    public static UserRegistrationResult Success { get; } = new(true, false, []);

    public static UserRegistrationResult Failure(bool loginTaken, IReadOnlyList<string> errors) =>
        new(false, loginTaken, errors);
}
