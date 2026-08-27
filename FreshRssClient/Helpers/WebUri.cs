using System.Net;

namespace FreshRssClient.Helpers;

public static class WebUri
{
    public static bool TryCreate(string? value, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool IsPublicHost(Uri uri)
    {
        var host = uri.DnsSafeHost.TrimEnd('.');
        if (host.Length == 0 || !host.Contains('.', StringComparison.Ordinal) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return true;
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] != 10 &&
                   !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                   !(bytes[0] == 192 && bytes[1] == 168) &&
                   !(bytes[0] == 169 && bytes[1] == 254);
        }

        return bytes.Length != 16 ||
               ((bytes[0] & 0xfe) != 0xfc && !(bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80));
    }
}
