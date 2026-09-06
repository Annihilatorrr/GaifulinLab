using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace GaifulinLab.Infrastructure.Analytics;

public sealed class ArticleViewVisitorHasher
{
    private const int MinimumHashKeyBytes = 32;
    private readonly byte[] _hashKey;

    public ArticleViewVisitorHasher(IConfiguration configuration)
        : this(ReadHashKey(configuration))
    {
    }

    internal ArticleViewVisitorHasher(string hashKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hashKey);

        _hashKey = Encoding.UTF8.GetBytes(hashKey);
        if (_hashKey.Length < MinimumHashKeyBytes)
        {
            throw new InvalidOperationException(
                $"Analytics view hash key must contain at least {MinimumHashKeyBytes} UTF-8 bytes.");
        }
    }

    public string Hash(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // A proxy may expose the same IPv4 visitor as an IPv4-mapped IPv6 address.
        // Folding it back avoids counting that visitor twice merely because of transport details.
        var normalizedAddress = address.IsIPv4MappedToIPv6
            ? address.MapToIPv4()
            : address;

        // We need a stable way to recognize repeat visits, not the address itself.
        // HMAC keeps the value unusable without the deployment secret.
        var hash = HMACSHA256.HashData(_hashKey, normalizedAddress.GetAddressBytes());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ReadHashKey(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hashKey = configuration["Analytics:ViewHashKey"];
        if (string.IsNullOrWhiteSpace(hashKey))
        {
            throw new InvalidOperationException(
                "Analytics setting 'Analytics:ViewHashKey' is not configured.");
        }

        return hashKey;
    }
}
