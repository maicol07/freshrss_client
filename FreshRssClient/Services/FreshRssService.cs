using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace FreshRssClient.Services
{
    public class RssFeed : ObservableObject
    {
        private string _id = string.Empty;
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string _htmlUrl = string.Empty;
        public string HtmlUrl
        {
            get => _htmlUrl;
            set => SetProperty(ref _htmlUrl, value);
        }

        private int _unreadCount;
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (SetProperty(ref _unreadCount, value))
                {
                    OnPropertyChanged(nameof(HasUnread));
                }
            }
        }

        public bool HasUnread => UnreadCount > 0;

        private string? _iconUrl;
        public string? IconUrl
        {
            get => _iconUrl;
            set
            {
                var normalized = string.IsNullOrEmpty(value) ? null : value;
                SetProperty(ref _iconUrl, normalized);
            }
        }
    }

    public class RssCategory : ObservableObject
    {
        private string _id = string.Empty;
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private int _unreadCount;
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (SetProperty(ref _unreadCount, value))
                {
                    OnPropertyChanged(nameof(HasUnread));
                }
            }
        }

        public bool HasUnread => UnreadCount > 0;

        private ObservableCollection<RssFeed> _feeds = new();
        public ObservableCollection<RssFeed> Feeds
        {
            get => _feeds;
            set => SetProperty(ref _feeds, value);
        }
    }

    public class RssArticle : ObservableObject
    {
        private string _id = string.Empty;
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string _link = string.Empty;
        public string Link
        {
            get => _link;
            set => SetProperty(ref _link, value);
        }

        private DateTime _publishDate = DateTime.Now;
        public DateTime PublishDate
        {
            get => _publishDate;
            set => SetProperty(ref _publishDate, value);
        }

        private string _summary = string.Empty;
        public string Summary
        {
            get => _summary;
            set => SetProperty(ref _summary, value);
        }

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        private string _feedTitle = string.Empty;
        public string FeedTitle
        {
            get => _feedTitle;
            set => SetProperty(ref _feedTitle, value);
        }

        private string _feedId = string.Empty;
        public string FeedId
        {
            get => _feedId;
            set => SetProperty(ref _feedId, value);
        }

        private string? _feedIconUrl;
        public string? FeedIconUrl
        {
            get => _feedIconUrl;
            set
            {
                var normalized = string.IsNullOrEmpty(value) ? null : value;
                SetProperty(ref _feedIconUrl, normalized);
            }
        }

        private bool _isRead;
        public bool IsRead
        {
            get => _isRead;
            set
            {
                if (SetProperty(ref _isRead, value))
                {
                    OnPropertyChanged(nameof(TitleFontWeight));
                    OnPropertyChanged(nameof(TitleOpacity));
                    OnPropertyChanged(nameof(MarkAsReadVisibility));
                }
            }
        }

        private string? _imageUrl;
        public string? ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (SetProperty(ref _imageUrl, value))
                {
                    OnPropertyChanged(nameof(ImageVisibility));
                }
            }
        }

        // Helper properties for direct XAML bindings
        public string TitleFontWeight => IsRead ? "Normal" : "SemiBold";
        public double TitleOpacity => IsRead ? 0.6 : 1.0;
        public Microsoft.UI.Xaml.Visibility ImageVisibility => string.IsNullOrEmpty(ImageUrl) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        public Microsoft.UI.Xaml.Visibility MarkAsReadVisibility => IsRead ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        private ICommand? _markAsReadCommand;
        [JsonIgnore]
        public ICommand? MarkAsReadCommand
        {
            get => _markAsReadCommand;
            set => SetProperty(ref _markAsReadCommand, value);
        }

        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    OnSelectionToggled?.Invoke(this);
                }
            }
        }

        [JsonIgnore]
        public Action<RssArticle>? OnSelectionToggled { get; set; }
    }

    public interface IFreshRssService
    {
        bool LastConnectionFailed { get; }
        Task<bool> AuthenticateAsync(string serverUrl, string username, string apiPassword, CancellationToken cancellationToken = default);
        Task<List<RssArticle>> FetchArticlesAsync(string? streamId, bool showUnreadOnly, int maxReadArticles, bool enableOpenGraphScrape, string? searchQuery = null, CancellationToken cancellationToken = default);
        Task<bool> MarkAsReadAsync(string articleId, CancellationToken cancellationToken = default);
        Task<bool> MarkAsUnreadAsync(string articleId, CancellationToken cancellationToken = default);
        Task<bool> MarkAllAsReadAsync(string? streamId, CancellationToken cancellationToken = default);
        Task<(List<RssCategory> Categories, List<RssFeed> Feeds)> FetchSubscriptionsAndUnreadCountsAsync(CancellationToken cancellationToken = default);
    }

    public class FreshRssService : IFreshRssService
    {
        private readonly HttpClient _httpClient;
        private readonly IOpenGraphService _openGraphService;
        
        private string _serverUrl = string.Empty;
        private string _username = string.Empty;
        private string _authToken = string.Empty;
        private bool _isAuthenticated = false;
        private bool _lastConnectionFailed = false;

        public bool LastConnectionFailed => _lastConnectionFailed;

        public FreshRssService(IOpenGraphService? openGraphService = null, HttpClient? httpClient = null)
        {
            _openGraphService = openGraphService ?? new OpenGraphService();
            _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<bool> AuthenticateAsync(string serverUrl, string username, string apiPassword, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(apiPassword))
            {
                return false;
            }

            // Ensure server URL formatting is correct
            serverUrl = serverUrl.Trim();
            if (!serverUrl.EndsWith("greader.php") && !serverUrl.EndsWith("greader.php/"))
            {
                serverUrl = serverUrl.TrimEnd('/') + "/api/greader.php";
            }
            serverUrl = serverUrl.TrimEnd('/');

            try
            {
                _lastConnectionFailed = false;
                var loginUrl = $"{serverUrl}/accounts/ClientLogin?Email={Uri.EscapeDataString(username)}&Passwd={Uri.EscapeDataString(apiPassword)}";
                var response = await _httpClient.GetAsync(loginUrl, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _isAuthenticated = false;
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // Parse Auth=xxxxx token
                var match = Regex.Match(content, @"Auth=([^\s\n]+)");
                if (match.Success && match.Groups.Count > 1)
                {
                    _authToken = match.Groups[1].Value;
                    _serverUrl = serverUrl;
                    _username = username;
                    _isAuthenticated = true;

                    // Set standard Authorization header for future requests
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("GoogleLogin", $"auth={_authToken}");
                    return true;
                }
            }
            catch (Exception)
            {
                _lastConnectionFailed = true;
            }

            _isAuthenticated = false;
            return false;
        }

        public async Task<List<RssArticle>> FetchArticlesAsync(string? streamId, bool showUnreadOnly, int maxReadArticles, bool enableOpenGraphScrape, string? searchQuery = null, CancellationToken cancellationToken = default)
        {
            if (!_isAuthenticated)
            {
                return new List<RssArticle>();
            }

            try
            {
                // We fetch a larger batch of articles if we want to include read ones
                int fetchCount = showUnreadOnly ? 100 : (100 + maxReadArticles);
                
                string targetStream = string.IsNullOrEmpty(streamId) ? "user/-/state/com.google/reading-list" : streamId;
                if (targetStream == "uncategorized")
                {
                    targetStream = "user/-/state/com.google/reading-list";
                }

                string escapedStream = targetStream;
                if (escapedStream.StartsWith("feed/"))
                {
                    escapedStream = "feed/" + Uri.EscapeDataString(escapedStream.Substring(5));
                }

                var url = $"{_serverUrl}/reader/api/0/stream/contents/{escapedStream}?output=json&n={fetchCount}";
                
                if (showUnreadOnly)
                {
                    url += $"&xt={Uri.EscapeDataString("user/-/state/com.google/read")}";
                }
                
                if (!string.IsNullOrEmpty(searchQuery))
                {
                    url += $"&q={Uri.EscapeDataString(searchQuery)}";
                }
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new List<RssArticle>();
                }

                var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                
                var readingList = JsonSerializer.Deserialize<GReaderStreamResponse>(jsonString, options);
                if (readingList?.Items == null)
                {
                    return new List<RssArticle>();
                }

                var list = new List<RssArticle>();
                int readCount = 0;

                foreach (var item in readingList.Items)
                {
                    bool isRead = item.Categories?.Contains("user/-/state/com.google/read") ?? false;

                    // If we only want unread, skip read ones (fallback check)
                    if (showUnreadOnly && isRead)
                    {
                        continue;
                    }

                    // If we are showing read ones, enforce the maxReadArticles limit
                    if (isRead)
                    {
                        if (readCount >= maxReadArticles)
                        {
                            continue; // Skip if we have reached our limit of read articles
                        }
                        readCount++;
                    }

                    string feedIconUrl = "ms-appx:///Assets/Square44x44Logo.targetsize-24_altform-unplated.png";
                    if (item.Origin != null && !string.IsNullOrEmpty(item.Origin.HtmlUrl))
                    {
                        try
                        {
                            var uri = new Uri(item.Origin.HtmlUrl);
                            feedIconUrl = $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=32";
                        }
                        catch { }
                    }

                    var article = new RssArticle
                    {
                        Id = item.Id,
                        Title = item.Title ?? "No Title",
                        Link = item.Alternate?[0]?.Href ?? item.Canonical?[0]?.Href ?? string.Empty,
                        PublishDate = ConvertTimestamp(item.Published),
                        Summary = item.Summary?.Content ?? string.Empty,
                        Content = item.Content?.Content ?? string.Empty,
                        FeedTitle = item.Origin?.Title ?? "Unknown Feed",
                        FeedId = item.Origin?.StreamId ?? string.Empty,
                        FeedIconUrl = feedIconUrl,
                        IsRead = isRead
                    };

                    // Extract image from body content if it exists
                    string? img = ExtractFirstImage(article.Content);
                    if (string.IsNullOrEmpty(img))
                    {
                        img = ExtractFirstImage(article.Summary);
                    }
                    article.ImageUrl = img;

                    // Clean tags from summary for card preview
                    if (!string.IsNullOrEmpty(article.Summary))
                    {
                        article.Summary = Regex.Replace(article.Summary, "<.*?>", string.Empty);
                        article.Summary = System.Net.WebUtility.HtmlDecode(article.Summary).Trim();
                    }

                    list.Add(article);
                }

                // Fetch via OpenGraph in parallel if enabled and image or description is missing
                if (enableOpenGraphScrape)
                {
                    var articlesNeedingScrape = list.Where(article => 
                        !string.IsNullOrEmpty(article.Link) && 
                        (string.IsNullOrEmpty(article.ImageUrl) || string.IsNullOrEmpty(article.Summary))
                    ).ToList();

                    if (articlesNeedingScrape.Count > 0)
                    {
                        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5 };
                        await Parallel.ForEachAsync(articlesNeedingScrape, parallelOptions, async (article, cancellationToken) =>
                        {
                            var og = await _openGraphService.FetchOpenGraphMetadataAsync(article.Link);
                            if (string.IsNullOrEmpty(article.ImageUrl) && !string.IsNullOrEmpty(og.ImageUrl))
                            {
                                article.ImageUrl = og.ImageUrl;
                            }
                            if (string.IsNullOrEmpty(article.Summary) && !string.IsNullOrEmpty(og.Description))
                            {
                                var cleanSummary = og.Description;
                                if (!string.IsNullOrEmpty(cleanSummary))
                                {
                                    cleanSummary = Regex.Replace(cleanSummary, "<.*?>", string.Empty);
                                    cleanSummary = System.Net.WebUtility.HtmlDecode(cleanSummary).Trim();
                                }
                                article.Summary = cleanSummary ?? string.Empty;
                            }
                        });
                    }
                }

                // Sort by publish date descending so newest are at the top
                list.Sort((a, b) => b.PublishDate.CompareTo(a.PublishDate));
                return list;
            }
            catch (Exception)
            {
                return new List<RssArticle>();
            }
        }

        public async Task<bool> MarkAsReadAsync(string articleId, CancellationToken cancellationToken = default)
        {
            if (!_isAuthenticated)
            {
                return false;
            }

            try
            {
                // GReader API marking read is done via POST to /reader/api/0/edit-tag
                var editTagUrl = $"{_serverUrl}/reader/api/0/edit-tag";
                
                var data = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("i", articleId),
                    new KeyValuePair<string, string>("a", "user/-/state/com.google/read")
                };

                var requestContent = new FormUrlEncodedContent(data);
                var response = await _httpClient.PostAsync(editTagUrl, requestContent, cancellationToken);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> MarkAsUnreadAsync(string articleId, CancellationToken cancellationToken = default)
        {
            if (!_isAuthenticated)
            {
                return false;
            }

            try
            {
                // GReader API marking unread is done via POST to /reader/api/0/edit-tag with r=...
                var editTagUrl = $"{_serverUrl}/reader/api/0/edit-tag";
                
                var data = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("i", articleId),
                    new KeyValuePair<string, string>("r", "user/-/state/com.google/read")
                };

                var requestContent = new FormUrlEncodedContent(data);
                var response = await _httpClient.PostAsync(editTagUrl, requestContent, cancellationToken);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> MarkAllAsReadAsync(string? streamId, CancellationToken cancellationToken = default)
        {
            if (!_isAuthenticated)
            {
                return false;
            }

            try
            {
                var markAllUrl = $"{_serverUrl}/reader/api/0/mark-all-as-read";
                
                string targetStream = string.IsNullOrEmpty(streamId) ? "user/-/state/com.google/reading-list" : streamId;
                if (targetStream == "uncategorized")
                {
                    targetStream = "user/-/state/com.google/reading-list";
                }

                long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var data = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("s", targetStream),
                    new KeyValuePair<string, string>("ts", ts.ToString())
                };

                var requestContent = new FormUrlEncodedContent(data);
                var response = await _httpClient.PostAsync(markAllUrl, requestContent, cancellationToken);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<(List<RssCategory> Categories, List<RssFeed> Feeds)> FetchSubscriptionsAndUnreadCountsAsync(CancellationToken cancellationToken = default)
        {
            if (!_isAuthenticated)
            {
                return (new List<RssCategory>(), new List<RssFeed>());
            }

            try
            {
                // 1. Fetch Subscription list and Unread counts in parallel
                var subUrl = $"{_serverUrl}/reader/api/0/subscription/list?output=json";
                var unreadUrl = $"{_serverUrl}/reader/api/0/unread-count?output=json";

                var subTask = _httpClient.GetAsync(subUrl, cancellationToken);
                var unreadTask = _httpClient.GetAsync(unreadUrl, cancellationToken);

                await Task.WhenAll(subTask, unreadTask);

                var subResponse = await subTask;
                if (!subResponse.IsSuccessStatusCode)
                {
                    return (new List<RssCategory>(), new List<RssFeed>());
                }

                var subJson = await subResponse.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var subData = JsonSerializer.Deserialize<GReaderSubscriptionResponse>(subJson, options);
                if (subData?.Subscriptions == null)
                {
                    return (new List<RssCategory>(), new List<RssFeed>());
                }

                var unreadResponse = await unreadTask;
                var unreadCountsMap = new Dictionary<string, int>();

                if (unreadResponse.IsSuccessStatusCode)
                {
                    var unreadJson = await unreadResponse.Content.ReadAsStringAsync(cancellationToken);
                    var unreadData = JsonSerializer.Deserialize<GReaderUnreadCountResponse>(unreadJson, options);
                    if (unreadData?.UnreadCounts != null)
                    {
                        foreach (var uc in unreadData.UnreadCounts)
                        {
                            unreadCountsMap[uc.Id] = uc.Count;
                        }
                    }
                }

                // 3. Process feeds and categories
                var categoriesDict = new Dictionary<string, RssCategory>();
                var allFeeds = new List<RssFeed>();
                var uncategorizedFeeds = new List<RssFeed>();

                foreach (var sub in subData.Subscriptions)
                {
                    // Calculate unread count for feed
                    unreadCountsMap.TryGetValue(sub.Id, out int feedUnread);

                    // Generate Favicon URL
                    string iconUrl = "ms-appx:///Assets/Square44x44Logo.targetsize-24_altform-unplated.png";
                    if (!string.IsNullOrEmpty(sub.HtmlUrl))
                    {
                        try
                        {
                            var uri = new Uri(sub.HtmlUrl);
                            iconUrl = $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=32";
                        }
                        catch { }
                    }

                    var feed = new RssFeed
                    {
                        Id = sub.Id,
                        Title = sub.Title,
                        HtmlUrl = sub.HtmlUrl ?? string.Empty,
                        UnreadCount = feedUnread,
                        IconUrl = iconUrl
                    };
                    allFeeds.Add(feed);

                    if (sub.Categories != null && sub.Categories.Count > 0)
                    {
                        foreach (var cat in sub.Categories)
                        {
                            if (!categoriesDict.TryGetValue(cat.Id, out var rssCat))
                            {
                                // Calculate unread count for category
                                unreadCountsMap.TryGetValue(cat.Id, out int catUnread);

                                rssCat = new RssCategory
                                {
                                    Id = cat.Id,
                                    Title = cat.Label,
                                    UnreadCount = catUnread
                                };
                                categoriesDict[cat.Id] = rssCat;
                            }
                            rssCat.Feeds.Add(feed);
                        }
                    }
                    else
                    {
                        uncategorizedFeeds.Add(feed);
                    }
                }

                var categoriesList = categoriesDict.Values.OrderBy(c => c.Title).ToList();

                // Handle Uncategorized group
                if (uncategorizedFeeds.Count > 0)
                {
                    string uncategorizedTitle = LocalizationManager.Current.UncategorizedGroup;
                    int uncategorizedUnread = uncategorizedFeeds.Sum(f => f.UnreadCount);

                    var uncategorizedCategory = new RssCategory
                    {
                        Id = "uncategorized",
                        Title = uncategorizedTitle,
                        UnreadCount = uncategorizedUnread
                    };
                    foreach (var feed in uncategorizedFeeds)
                    {
                        uncategorizedCategory.Feeds.Add(feed);
                    }
                    categoriesList.Add(uncategorizedCategory);
                }

                return (categoriesList, allFeeds);
            }
            catch
            {
                return (new List<RssCategory>(), new List<RssFeed>());
            }
        }

        private DateTime ConvertTimestamp(long timestampSeconds)
        {
            if (timestampSeconds == 0) return DateTime.Now;
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(timestampSeconds).LocalDateTime;
            }
            catch
            {
                // GReader API might return milliseconds in some cases
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(timestampSeconds).LocalDateTime;
                }
                catch
                {
                    return DateTime.Now;
                }
            }
        }

        private string? ExtractFirstImage(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            try
            {
                var match = Regex.Match(html, @"<img\s+[^>]*src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch
            {
                // Ignore Regex errors
            }
            return null;
        }

        #region GReader API JSON Classes

        private class GReaderSubscriptionResponse
        {
            public List<GReaderSubscription>? Subscriptions { get; set; }
        }

        private class GReaderSubscription
        {
            public string Id { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string? HtmlUrl { get; set; }
            public List<GReaderCategory>? Categories { get; set; }
        }

        private class GReaderCategory
        {
            public string Id { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
        }

        private class GReaderUnreadCountResponse
        {
            public List<GReaderUnreadCount>? UnreadCounts { get; set; }
        }

        private class GReaderUnreadCount
        {
            public string Id { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        private class GReaderStreamResponse
        {
            public string Id { get; set; } = string.Empty;
            public string? Title { get; set; }
            public List<GReaderItem>? Items { get; set; }
        }

        private class GReaderItem
        {
            public string Id { get; set; } = string.Empty;
            public string? Title { get; set; }
            public long Published { get; set; }
            public List<GReaderLink>? Alternate { get; set; }
            public List<GReaderLink>? Canonical { get; set; }
            public GReaderContent? Summary { get; set; }
            public GReaderContent? Content { get; set; }
            public GReaderOrigin? Origin { get; set; }
            public List<string>? Categories { get; set; }
        }

        private class GReaderLink
        {
            public string Href { get; set; } = string.Empty;
            public string? Type { get; set; }
        }

        private class GReaderContent
        {
            public string Content { get; set; } = string.Empty;
        }

        private class GReaderOrigin
        {
            public string StreamId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string? HtmlUrl { get; set; }
        }

        #endregion
    }
}
