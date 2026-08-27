namespace FreshRssClient.Helpers;

public static class WebUri
{
    public static bool TryCreate(string? value, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
