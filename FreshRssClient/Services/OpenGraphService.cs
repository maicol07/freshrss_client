using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FreshRssClient.Services
{
    public interface IOpenGraphService
    {
        Task<(string? Description, string? ImageUrl)> FetchOpenGraphMetadataAsync(string articleUrl);
    }

    public class OpenGraphService : IOpenGraphService
    {
        private readonly HttpClient _httpClient;

        public OpenGraphService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                AllowAutoRedirect = true
            });
            
            // Add a common User-Agent header so sites do not block the request
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        public async Task<(string? Description, string? ImageUrl)> FetchOpenGraphMetadataAsync(string articleUrl)
        {
            if (string.IsNullOrWhiteSpace(articleUrl) || !Uri.TryCreate(articleUrl, UriKind.Absolute, out _))
            {
                return (null, null);
            }

            try
            {
                // Fetch the HTML content
                var response = await _httpClient.GetAsync(articleUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return (null, null);
                }

                var html = await response.Content.ReadAsStringAsync();
                
                string? imageUrl = ExtractTagContent(html, @"<meta\s+[^>]*property=[""']og:image[""']\s+[^>]*content=[""']([^""']+)[""']");
                if (string.IsNullOrEmpty(imageUrl))
                {
                    imageUrl = ExtractTagContent(html, @"<meta\s+[^>]*content=[""']([^""']+)[""']\s+[^>]*property=[""']og:image[""']");
                }
                if (string.IsNullOrEmpty(imageUrl))
                {
                    imageUrl = ExtractTagContent(html, @"<meta\s+[^>]*name=[""']twitter:image[""']\s+[^>]*content=[""']([^""']+)[""']");
                }

                string? description = ExtractTagContent(html, @"<meta\s+[^>]*property=[""']og:description[""']\s+[^>]*content=[""']([^""']+)[""']");
                if (string.IsNullOrEmpty(description))
                {
                    description = ExtractTagContent(html, @"<meta\s+[^>]*content=[""']([^""']+)[""']\s+[^>]*property=[""']og:description[""']");
                }
                if (string.IsNullOrEmpty(description))
                {
                    description = ExtractTagContent(html, @"<meta\s+[^>]*name=[""']description[""']\s+[^>]*content=[""']([^""']+)[""']");
                }

                // Decode HTML entities if parsed
                if (description != null)
                {
                    description = System.Net.WebUtility.HtmlDecode(description).Trim();
                }
                if (imageUrl != null)
                {
                    imageUrl = System.Net.WebUtility.HtmlDecode(imageUrl).Trim();
                }

                return (description, imageUrl);
            }
            catch (Exception)
            {
                // Silence errors to fail gracefully when OpenGraph scraping fails
                return (null, null);
            }
        }

        private string? ExtractTagContent(string html, string pattern)
        {
            try
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value;
                }
            }
            catch
            {
                // Regex timeout or syntax issue
            }
            return null;
        }
    }
}
