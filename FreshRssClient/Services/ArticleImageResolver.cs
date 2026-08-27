using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FreshRssClient.Helpers;

namespace FreshRssClient.Services
{
    public interface IArticleImageResolver
    {
        Task<string?> ResolveAsync(string? articleLink, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Best-effort resolution of an article image from the social/meta tags of the linked web page
    /// (Open Graph, Twitter Cards, link rel="image_src").
    /// Uses its own HttpClient so the FreshRSS auth token is never sent to third-party sites.
    /// </summary>
    public class ArticleImageResolver : IArticleImageResolver
    {
        private const int MaxHtmlBytes = 256 * 1024;
        private const int MaxCacheEntries = 2000;
        private const string LogTag = "ArticleImageResolver";

        // Publishers behind CDNs routinely reject unknown agents with 403, which kills the whole feature.
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 FreshRssClient/1.0";

        // Highest priority first.
        private static readonly string[] MetaKeyPriority =
        {
            "og:image:secure_url",
            "og:image",
            "og:image:url",
            "twitter:image",
            "twitter:image:src",
            "image_src"
        };

        private static readonly Regex MetaTagRegex = new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LinkTagRegex = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MetaKeyRegex = new(@"(?:property|name|itemprop)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ContentRegex = new(@"content\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RelRegex = new(@"rel\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HrefRegex = new(@"href\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly HttpClient _httpClient;

        // Caches resolved images per page URL, including failures (null) to avoid re-scraping image-less articles.
        private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);

        public ArticleImageResolver(HttpClient? httpClient = null)
        {
            if (httpClient != null)
            {
                _httpClient = httpClient;
                return;
            }

            _httpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            })
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("it-IT,it;q=0.9,en;q=0.8");
        }

        public async Task<string?> ResolveAsync(string? articleLink, CancellationToken cancellationToken = default)
        {
            if (!WebUri.TryCreate(articleLink, out var pageUri) || !WebUri.IsPublicHost(pageUri))
            {
                DebugLog.Write(LogTag, $"skip: link non utilizzabile '{articleLink}'");
                return null;
            }

            string cacheKey = pageUri.AbsoluteUri;
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                DebugLog.Write(LogTag, $"cache hit {cacheKey} -> {cached ?? "<null>"}");
                return cached;
            }

            string? resolved = null;
            try
            {
                resolved = await FetchSocialImageAsync(pageUri, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DebugLog.Write(LogTag, $"annullato dal chiamante {cacheKey}");
                throw;
            }
            catch (OperationCanceledException)
            {
                DebugLog.Write(LogTag, $"timeout richiesta {cacheKey}");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                DebugLog.Write(LogTag, $"errore rete {cacheKey}: {ex.GetType().Name} {ex.Message}");
            }

            DebugLog.Write(LogTag, $"risolto {cacheKey} -> {resolved ?? "<nessuna immagine>"}");

            if (_cache.Count >= MaxCacheEntries)
            {
                _cache.Clear();
            }

            _cache[cacheKey] = resolved;
            return resolved;
        }

        private async Task<string?> FetchSocialImageAsync(Uri pageUri, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                DebugLog.Write(LogTag, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} per {pageUri}");
                return null;
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            {
                DebugLog.Write(LogTag, $"content-type scartato '{mediaType}' per {pageUri}");
                return null;
            }

            // Redirects may land on a private host.
            var finalUri = response.RequestMessage?.RequestUri ?? pageUri;
            if (!WebUri.IsPublicHost(finalUri))
            {
                DebugLog.Write(LogTag, $"redirect verso host non pubblico {finalUri}");
                return null;
            }

            string head = await ReadHeadAsync(response, cancellationToken);
            DebugLog.Write(LogTag, $"letti {head.Length} caratteri da {finalUri}, </head> trovato: {head.Contains("</head>", StringComparison.OrdinalIgnoreCase)}, contiene og:image: {head.Contains("og:image", StringComparison.OrdinalIgnoreCase)}");

            return ExtractSocialImage(head, finalUri);
        }

        /// <summary>
        /// Reads the response body until the closing head tag is seen, capped to avoid pulling whole pages.
        /// </summary>
        private static async Task<string> ReadHeadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var builder = new StringBuilder();
            var buffer = new byte[8 * 1024];
            int total = 0;

            while (total < MaxHtmlBytes)
            {
                int chunk = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxHtmlBytes - total)), cancellationToken);
                if (chunk == 0)
                {
                    break;
                }

                total += chunk;
                builder.Append(Encoding.UTF8.GetString(buffer, 0, chunk));

                if (builder.ToString().Contains("</head>", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Extracts the best social image URL from HTML markup, resolved against the page URL.
        /// </summary>
        public static string? ExtractSocialImage(string html, Uri pageUri)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }

            var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match tag in MetaTagRegex.Matches(html))
            {
                var key = MetaKeyRegex.Match(tag.Value);
                var content = ContentRegex.Match(tag.Value);
                if (key.Success && content.Success)
                {
                    AddCandidate(candidates, key.Groups[1].Value.Trim(), content.Groups[1].Value);
                }
            }

            foreach (Match tag in LinkTagRegex.Matches(html))
            {
                var rel = RelRegex.Match(tag.Value);
                var href = HrefRegex.Match(tag.Value);
                if (rel.Success && href.Success)
                {
                    AddCandidate(candidates, rel.Groups[1].Value.Trim(), href.Groups[1].Value);
                }
            }

            var relevantKeys = candidates.Keys
                .Where(key => key.StartsWith("og:", StringComparison.OrdinalIgnoreCase) ||
                              key.StartsWith("twitter:", StringComparison.OrdinalIgnoreCase) ||
                              key.Equals("image_src", StringComparison.OrdinalIgnoreCase))
                .ToList();
            DebugLog.Write(LogTag, $"candidati immagine: {(relevantKeys.Count == 0 ? "<nessuno>" : string.Join(", ", relevantKeys))}");

            foreach (var metaKey in MetaKeyPriority)
            {
                if (!candidates.TryGetValue(metaKey, out var value))
                {
                    continue;
                }

                if (TryBuildAbsoluteUrl(value, pageUri, out var absolute))
                {
                    return absolute;
                }

                DebugLog.Write(LogTag, $"valore non utilizzabile per {metaKey}: '{value}'");
            }

            return null;
        }

        private static void AddCandidate(Dictionary<string, string> candidates, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || candidates.ContainsKey(key))
            {
                return;
            }
            candidates[key] = value.Trim();
        }

        private static bool TryBuildAbsoluteUrl(string value, Uri pageUri, out string absolute)
        {
            absolute = string.Empty;
            string decoded = System.Net.WebUtility.HtmlDecode(value).Trim();
            if (decoded.Length == 0)
            {
                return false;
            }

            if (!Uri.TryCreate(pageUri, decoded, out var candidate) ||
                (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            absolute = candidate.AbsoluteUri;
            return true;
        }
    }
}
