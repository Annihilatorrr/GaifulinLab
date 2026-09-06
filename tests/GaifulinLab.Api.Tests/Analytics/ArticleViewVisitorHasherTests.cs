using System.Net;
using GaifulinLab.Infrastructure.Analytics;
using Microsoft.Extensions.Configuration;

namespace GaifulinLab.Api.Tests.Analytics;

public sealed class ArticleViewVisitorHasherTests
{
    [Fact]
    public void Hash_TreatsIpv4MappedIpv6AsTheEquivalentIpv4Address()
    {
        var hasher = CreateHasher();

        var ipv4Hash = hasher.Hash(IPAddress.Parse("203.0.113.10"));
        var mappedIpv6Hash = hasher.Hash(IPAddress.Parse("::ffff:203.0.113.10"));

        Assert.Equal(ipv4Hash, mappedIpv6Hash);
        Assert.Matches("^[a-f0-9]{64}$", ipv4Hash);
    }

    [Fact]
    public void Hash_ProducesDifferentValuesForDifferentAddresses()
    {
        var hasher = CreateHasher();

        var firstHash = hasher.Hash(IPAddress.Parse("2001:db8::1"));
        var secondHash = hasher.Hash(IPAddress.Parse("2001:db8::2"));

        Assert.NotEqual(firstHash, secondHash);
    }

    [Fact]
    public void Constructor_RejectsMissingHashKey()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new ArticleViewVisitorHasher(configuration));
    }

    private static ArticleViewVisitorHasher CreateHasher() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analytics:ViewHashKey"] = "test-view-hash-key-that-is-at-least-32-bytes-long"
            })
            .Build());
}
