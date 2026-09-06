using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;

namespace GaifulinLab.Api.Configuration;

internal static class ApiForwardedHeadersConfiguration
{
    public static ForwardedHeadersOptions Create(IConfiguration configuration)
    {
        var trustedNetworks = configuration
            .GetSection("ForwardedHeaders:TrustedNetworks")
            .Get<string[]>()
            ?? [];
        if (trustedNetworks.Length == 0 || trustedNetworks.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "At least one trusted proxy network must be configured in 'ForwardedHeaders:TrustedNetworks'.");
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            // A public request reaches the API through host nginx and web nginx.
            ForwardLimit = 2
        };
        // A client can forge X-Forwarded-For. It is meaningful only after a connection
        // from one of our proxy networks, so do not retain ASP.NET's broad defaults.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var trustedNetwork in trustedNetworks)
        {
            options.KnownIPNetworks.Add(ParseNetwork(trustedNetwork));
        }

        return options;
    }

    private static System.Net.IPNetwork ParseNetwork(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            throw new InvalidOperationException(
                $"Trusted proxy network '{value}' must be a valid CIDR range.");
        }

        var maximumPrefixLength = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => 32,
            AddressFamily.InterNetworkV6 => 128,
            _ => 0
        };
        if (maximumPrefixLength == 0 || prefixLength < 0 || prefixLength > maximumPrefixLength)
        {
            throw new InvalidOperationException(
                $"Trusted proxy network '{value}' has an invalid prefix length.");
        }

        return new System.Net.IPNetwork(address, prefixLength);
    }
}
