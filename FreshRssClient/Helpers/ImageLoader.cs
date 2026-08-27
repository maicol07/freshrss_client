using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace FreshRssClient.Helpers
{
    /// <summary>
    /// Creates bitmaps for article images, reporting outcomes to the debug log.
    /// Some CDNs (Cloudflare bot management) reject the WinINet stack behind BitmapImage.UriSource while
    /// accepting plain HttpClient requests, so hosts that fail once are loaded through HttpClient instead.
    /// </summary>
    public static class ImageLoader
    {
        private const string LogTag = "ImageLoader";
        private const int MaxCachedImages = 64;
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        private static readonly HttpClient Client = CreateClient();
        private static readonly ConcurrentDictionary<string, byte[]> ByteCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentQueue<string> CacheOrder = new();
        private static readonly ConcurrentDictionary<string, bool> HostsNeedingFallback = new(StringComparer.OrdinalIgnoreCase);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

        public static BitmapImage? Create(string? url)
        {
            // Any scheme BitmapImage understands is fine here (http/https, ms-appx, ms-appdata):
            // restricting to web URLs belongs to the scraper, not to the renderer.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                DebugLog.Write(LogTag, $"URL non utilizzabile: '{url}'");
                return null;
            }

            var bitmap = new BitmapImage();
            bool isWebImage = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            var dispatcher = DispatcherQueue.GetForCurrentThread();

            if (isWebImage && HostsNeedingFallback.ContainsKey(uri.Host))
            {
                SafeFireAndForget.Run(() => LoadThroughHttpClientAsync(bitmap, uri, dispatcher));
                return bitmap;
            }

            bitmap.ImageOpened += (_, _) => DebugLog.Write(LogTag, $"caricata {uri}");
            bitmap.ImageFailed += (_, args) =>
            {
                DebugLog.Write(LogTag, $"caricamento fallito {uri}: {args.ErrorMessage}");
                if (isWebImage)
                {
                    HostsNeedingFallback[uri.Host] = true;
                    SafeFireAndForget.Run(() => LoadThroughHttpClientAsync(bitmap, uri, dispatcher));
                }
            };
            bitmap.UriSource = uri;
            return bitmap;
        }

        private static async Task LoadThroughHttpClientAsync(BitmapImage bitmap, Uri uri, DispatcherQueue? dispatcher)
        {
            byte[]? bytes = await GetBytesAsync(uri);
            if (bytes == null)
            {
                return;
            }

            if (dispatcher == null)
            {
                DebugLog.Write(LogTag, $"nessun dispatcher disponibile, immagine scaricata ma non applicata: {uri}");
                return;
            }

            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    using var stream = new InMemoryRandomAccessStream();
                    var writer = new DataWriter(stream);
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                    stream.Seek(0);

                    await bitmap.SetSourceAsync(stream);
                    DebugLog.Write(LogTag, $"caricata via HttpClient {uri}");
                }
                catch (Exception ex)
                {
                    DebugLog.Write(LogTag, $"decodifica fallita {uri}: {ex.GetType().Name} {ex.Message}");
                }
            });
        }

        private static async Task<byte[]?> GetBytesAsync(Uri uri)
        {
            string cacheKey = uri.AbsoluteUri;
            if (ByteCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            try
            {
                byte[] bytes = await Client.GetByteArrayAsync(uri);
                Cache(cacheKey, bytes);
                return bytes;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                DebugLog.Write(LogTag, $"download fallito {uri}: {ex.GetType().Name} {ex.Message}");
                return null;
            }
        }

        private static void Cache(string cacheKey, byte[] bytes)
        {
            if (!ByteCache.TryAdd(cacheKey, bytes))
            {
                return;
            }

            CacheOrder.Enqueue(cacheKey);
            while (CacheOrder.Count > MaxCachedImages && CacheOrder.TryDequeue(out var oldest))
            {
                ByteCache.TryRemove(oldest, out _);
            }
        }
    }
}
