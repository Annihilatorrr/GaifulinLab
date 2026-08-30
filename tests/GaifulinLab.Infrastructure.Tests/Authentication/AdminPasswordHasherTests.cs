using GaifulinLab.Infrastructure.Authentication;

namespace GaifulinLab.Infrastructure.Tests.Authentication;

public sealed class AdminPasswordHasherTests
{
    [Fact]
    public void HashAndVerify_AcceptsCorrectPassword()
    {
        var hasher = new AdminPasswordHasher();
        var passwordHash = hasher.Hash("correct-horse-battery-staple");

        Assert.True(hasher.Verify(passwordHash, "correct-horse-battery-staple"));
        Assert.False(hasher.Verify(passwordHash, "wrong-password"));
        Assert.NotEqual("correct-horse-battery-staple", passwordHash);
    }

    [Fact]
    public void Verify_RejectsMalformedHash()
    {
        var hasher = new AdminPasswordHasher();

        Assert.False(hasher.Verify("not-a-password-hash", "password"));
    }
}
