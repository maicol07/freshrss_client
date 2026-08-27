using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FreshRssClient.Models;
using FreshRssClient.Services;
using FreshRssClient.Helpers;

namespace FreshRssClient.ViewModels
{
    public class MainViewModel : ObservableObject, IDisposable
    {
        private const string ImageLogTag = "ImageEnrichment";
        private const int MaxImageEnrichmentPerSync = 25;
        private const int ImageEnrichmentConcurrency = 4;
        private const int ImageEnrichmentPassTimeoutSeconds = 30;

        private readonly IFreshRssService _freshRssService;
        private readonly INotificationService _notificationService;
        private readonly IArticleImageResolver _articleImageResolver;
        private readonly ILocalStore _localStore;
        private readonly DispatcherQueue? _dispatcherQueue;
        private CancellationTokenSource? _syncCts;
        private CancellationTokenSource? _syncTimerCts;
        private CancellationTokenSource? _imageEnrichmentCts;
        private readonly object _syncLock = new();
        private TrayIconHelper? _trayIconHelper;
        private HashSet<string> _sentNotificationIds = new();

        // Images resolved from the web (or already present in the offline cache), so a later sync
        // cannot overwrite them with the image-less articles coming from the server.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _knownImagesByArticleId = new(StringComparer.Ordinal);

        private CancellationToken CancelAndGetNewToken()
        {
            lock (_syncLock)
            {
                try
                {
                    _syncCts?.Cancel();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CancelAndGetNewToken] Cancel failed: {ex.Message}");
                }

                _syncCts = new CancellationTokenSource();
                return _syncCts.Token;
            }
        }

        // Settings Form Properties
        private string _serverUrl = string.Empty;
        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                if (SetProperty(ref _serverUrl, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        private string _apiPassword = string.Empty;
        public string ApiPassword
        {
            get => _apiPassword;
            set
            {
                if (SetProperty(ref _apiPassword, value))
                {
                    SaveAndApplySettings(reconnect: true);
                }
            }
        }

        private int _syncInterval = 15; // default 15 mins
        public int SyncInterval
        {
            get => _syncInterval;
            set
            {
                if (SetProperty(ref _syncInterval, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        private bool _showUnreadOnly = false; // default to showing all (Tutti)
        public bool ShowUnreadOnly
        {
            get => _showUnreadOnly;
            set
            {
                if (SetProperty(ref _showUnreadOnly, value))
                {
                    _articleFilter = value ? "Unread" : "All";
                    OnPropertyChanged(nameof(ArticleFilter));
                    SaveAndApplySettings(reconnect: false);
                    // Automatically trigger sync when toggling to refresh list
                    SafeFireAndForget.Run(() => SyncFeedsAsync());
                }
            }
        }

        private string _articleFilter = "All"; // "Unread", "Read", "All"
        public string ArticleFilter
        {
            get => _articleFilter;
            set
            {
                if (SetProperty(ref _articleFilter, value))
                {
                    bool needsSync = false;
                    if (value == "Unread" && !ShowUnreadOnly)
                    {
                        _showUnreadOnly = true;
                        needsSync = true;
                    }
                    else if (value != "Unread" && ShowUnreadOnly)
                    {
                        _showUnreadOnly = false;
                        needsSync = true;
                    }

                    OnPropertyChanged(nameof(ShowUnreadOnly));

                    SaveAndApplySettings(reconnect: false);

                    if (needsSync)
                    {
                        SafeFireAndForget.Run(() => SyncFeedsAsync());
                    }
                    else
                    {
                        LoadCachedArticlesForActiveStream();
                    }
                }
            }
        }

        private bool _isMultiSelectMode = false;
        private bool _suppressSelectionCallback = false;
        public bool IsMultiSelectMode
        {
            get => _isMultiSelectMode;
            set
            {
                if (SetProperty(ref _isMultiSelectMode, value))
                {
                    if (!value)
                    {
                        _suppressSelectionCallback = true;
                        try
                        {
                            foreach (var article in Articles)
                            {
                                article.IsSelected = false;
                            }
                        }
                        finally
                        {
                            _suppressSelectionCallback = false;
                        }
                        SelectedArticles.Clear();
                        OnPropertyChanged(nameof(SelectedArticlesCountText));
                        OnPropertyChanged(nameof(HasSelectedArticles));
                    }
                    OnPropertyChanged(nameof(IsMultiSelectActive));
                }
            }
        }

        public bool IsMultiSelectActive => _isMultiSelectMode;

        public List<RssArticle> SelectedArticles { get; } = new();
        public string SelectedArticlesCountText => $"{SelectedArticles.Count} {LocalizationManager.Current.SelectedArticlesSuffix}";
        public bool HasSelectedArticles => SelectedArticles.Count > 0;

        public void SetSelectedArticles(List<RssArticle> articles)
        {
            _suppressSelectionCallback = true;
            try
            {
                foreach (var article in Articles)
                {
                    article.IsSelected = false;
                }
                SelectedArticles.Clear();
                SelectedArticles.AddRange(articles);
                foreach (var article in articles)
                {
                    article.IsSelected = true;
                }
            }
            finally
            {
                _suppressSelectionCallback = false;
            }
            OnPropertyChanged(nameof(SelectedArticlesCountText));
            OnPropertyChanged(nameof(HasSelectedArticles));
        }

        public void ToggleSelectAll()
        {
            bool allSelected = Articles.Count > 0 && Articles.All(a => a.IsSelected);
            
            _suppressSelectionCallback = true;
            try
            {
                foreach (var article in Articles)
                {
                    article.IsSelected = !allSelected;
                }
                
                SelectedArticles.Clear();
                if (!allSelected)
                {
                    SelectedArticles.AddRange(Articles);
                    IsMultiSelectMode = true;
                }
                else
                {
                    IsMultiSelectMode = false;
                }
            }
            finally
            {
                _suppressSelectionCallback = false;
            }
            
            OnPropertyChanged(nameof(SelectedArticlesCountText));
            OnPropertyChanged(nameof(HasSelectedArticles));
            OnPropertyChanged(nameof(IsMultiSelectMode));
        }

        private void AttachCommands(RssArticle article)
        {
            article.MarkAsReadCommand = new RelayCommand(() => SafeFireAndForget.Run(() => MarkArticleAsReadAsync(article)));
            article.OnSelectionToggled = HandleArticleSelectionToggled;
        }

        private void HandleArticleSelectionToggled(RssArticle article)
        {
            if (_suppressSelectionCallback) return;

            if (article.IsSelected)
            {
                if (!SelectedArticles.Contains(article))
                    SelectedArticles.Add(article);
            }
            else
            {
                SelectedArticles.Remove(article);
            }

            if (SelectedArticles.Count > 0)
                IsMultiSelectMode = true;
            else
                IsMultiSelectMode = false;

            OnPropertyChanged(nameof(SelectedArticlesCountText));
            OnPropertyChanged(nameof(HasSelectedArticles));
        }

        private int _maxReadArticles = 50; // default 50 read articles
        public int MaxReadArticles
        {
            get => _maxReadArticles;
            set
            {
                if (SetProperty(ref _maxReadArticles, value))
                {
                    SaveAndApplySettings(reconnect: false);
                    // Automatically trigger sync when toggling to refresh list
                    SafeFireAndForget.Run(() => SyncFeedsAsync());
                }
            }
        }

        private string _language = "it"; // "it" or "en"
        public string Language
        {
            get => _language;
            set
            {
                if (SetProperty(ref _language, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        private bool _useGridLayout = false;
        public bool UseGridLayout
        {
            get => _useGridLayout;
            set
            {
                if (SetProperty(ref _useGridLayout, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        private bool _openLinksInBrowser = false;
        public bool OpenLinksInBrowser
        {
            get => _openLinksInBrowser;
            set
            {
                if (SetProperty(ref _openLinksInBrowser, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        private bool _fetchMissingImagesFromWeb = true;
        public bool FetchMissingImagesFromWeb
        {
            get => _fetchMissingImagesFromWeb;
            set
            {
                if (SetProperty(ref _fetchMissingImagesFromWeb, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        private bool _autoStartWithWindows = false;
        public bool AutoStartWithWindows
        {
            get => _autoStartWithWindows;
            set
            {
                if (SetProperty(ref _autoStartWithWindows, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        private bool _startMinimizedInTray = false;
        public bool StartMinimizedInTray
        {
            get => _startMinimizedInTray;
            set
            {
                if (SetProperty(ref _startMinimizedInTray, value))
                {
                    SaveAndApplySettings(reconnect: false);
                }
            }
        }

        // Status & UI Properties
        private string _syncStatusText = string.Empty;
        public string SyncStatusText
        {
            get => _syncStatusText;
            set => SetProperty(ref _syncStatusText, value);
        }

        private string _connectionStatusText = string.Empty;
        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set => SetProperty(ref _connectionStatusText, value);
        }

        private bool _isSyncing = false;
        public bool IsSyncing
        {
            get => _isSyncing;
            set => SetProperty(ref _isSyncing, value);
        }

        private int _unreadCount = 0;
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (SetProperty(ref _unreadCount, value))
                {
                    _trayIconHelper?.UpdateUnreadCount(value);
                    _notificationService.UpdateBadge(value);
                    OnPropertyChanged(nameof(UnreadCountHeaderText));
                }
            }
        }

        public string UnreadCountHeaderText => $"{LocalizationManager.Current.UnreadArticlesHeader} ({UnreadCount})";

        // Lists
        public ObservableCollection<RssArticle> Articles { get; } = new();

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplyLocalSearch();
                }
            }
        }

        private readonly List<RssArticle> _currentAllArticles = new();

        private void ApplyLocalSearch()
        {
            var query = SearchQuery?.Trim() ?? string.Empty;
            IEnumerable<RssArticle> filtered = _currentAllArticles;

            filtered = ArticleFilter switch
            {
                "Read" => filtered.Where(article => article.IsRead),
                "Unread" => filtered.Where(article => !article.IsRead),
                _ => filtered
            };

            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(a =>
                    a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (a.Summary != null && a.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (a.Content != null && a.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (a.FeedTitle != null && a.FeedTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                );
            }

            UpdateDisplayedArticles(filtered.ToList());
        }

        private void UpdateDisplayedArticles(List<RssArticle> filteredList)
        {
            // Smart articles update in place to avoid clearing and causing UI flickering
            for (int i = 0; i < filteredList.Count; i++)
            {
                var fetched = filteredList[i];
                if (i < Articles.Count)
                {
                    var existing = Articles[i];
                    if (existing.Id == fetched.Id)
                    {
                        // Update properties in place if changed
                        existing.IsRead = fetched.IsRead;
                        existing.Title = fetched.Title;
                        existing.Summary = fetched.Summary;
                        existing.Content = fetched.Content;
                        // Keep a web-resolved image if the fetched article has none
                        if (!string.IsNullOrEmpty(fetched.ImageUrl))
                        {
                            existing.ImageUrl = fetched.ImageUrl;
                        }
                        existing.FeedTitle = fetched.FeedTitle;
                        existing.FeedIconUrl = fetched.FeedIconUrl;
                        existing.PublishDate = fetched.PublishDate;
                        AttachCommands(existing);
                    }
                    else
                    {
                        var matchInRemaining = Articles.Skip(i).FirstOrDefault(a => a.Id == fetched.Id);
                        if (matchInRemaining != null)
                        {
                            while (Articles[i].Id != fetched.Id)
                            {
                                Articles.RemoveAt(i);
                            }
                            Articles[i].IsRead = fetched.IsRead;
                            Articles[i].Title = fetched.Title;
                            Articles[i].Summary = fetched.Summary;
                            Articles[i].Content = fetched.Content;
                            if (!string.IsNullOrEmpty(fetched.ImageUrl))
                            {
                                Articles[i].ImageUrl = fetched.ImageUrl;
                            }
                            Articles[i].FeedTitle = fetched.FeedTitle;
                            Articles[i].FeedIconUrl = fetched.FeedIconUrl;
                            Articles[i].PublishDate = fetched.PublishDate;
                            AttachCommands(Articles[i]);
                        }
                        else
                        {
                            Articles.Insert(i, fetched);
                        }
                    }
                }
                else
                {
                    Articles.Add(fetched);
                }
            }

            while (Articles.Count > filteredList.Count)
            {
                Articles.RemoveAt(Articles.Count - 1);
            }
        }

        public ObservableCollection<RssCategory> Categories { get; } = new();

        private RssCategory? _selectedCategory;
        public RssCategory? SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        private RssFeed? _selectedFeed;
        public RssFeed? SelectedFeed
        {
            get => _selectedFeed;
            set => SetProperty(ref _selectedFeed, value);
        }

        private string? _activeStreamId;
        public string? ActiveStreamId
        {
            get => _activeStreamId;
            set => SetProperty(ref _activeStreamId, value);
        }

        public event EventHandler? NavigationStructureChanged;

        public void SelectCategory(RssCategory category)
        {
            SelectedArticle = null;
            SelectedFeed = null;
            SelectedCategory = category;
            ActiveStreamId = category.Id;
            LoadCachedArticlesForActiveStream();
            if (!IsSyncing)
            {
                SafeFireAndForget.Run(() => SyncFeedsAsync());
            }
        }

        public void SelectFeed(RssFeed feed)
        {
            SelectedArticle = null;
            SelectedCategory = null;
            SelectedFeed = feed;
            ActiveStreamId = feed.Id;
            LoadCachedArticlesForActiveStream();
            if (!IsSyncing)
            {
                SafeFireAndForget.Run(() => SyncFeedsAsync());
            }
        }

        public async Task SelectAllArticlesAsync()
        {
            SelectedArticle = null;
            SelectedCategory = null;
            SelectedFeed = null;
            ActiveStreamId = null;
            LoadCachedArticlesForActiveStream();
            if (!IsSyncing)
            {
                await SyncFeedsAsync();
            }
        }

        public void SelectArticleById(string articleId)
        {
            var article = Articles.FirstOrDefault(a => a.Id == articleId);
            if (article != null)
                SelectedArticle = article;
        }

        private RssArticle? _selectedArticle;
        public RssArticle? SelectedArticle
        {
            get => _selectedArticle;
            set
            {
                if (IsMultiSelectMode) return;

                if (value != null && OpenLinksInBrowser)
                {
                    SafeFireAndForget.Run(() => MarkAsReadAndOpenBrowserAsync(value));
                    _selectedArticle = null;
                    OnPropertyChanged(nameof(SelectedArticle));
                    return;
                }

                if (SetProperty(ref _selectedArticle, value) && value != null)
                {
                    // Mark as read asynchronously
                    SafeFireAndForget.Run(() => MarkArticleAsReadAsync(value));
                }
            }
        }

        // Commands
        public IAsyncRelayCommand SyncCommand { get; }
        public IRelayCommand SaveSettingsCommand { get; }

        private void EnqueueOnDispatcher(Action action)
        {
            if (_dispatcherQueue != null)
            {
                _dispatcherQueue.TryEnqueue(() => action());
            }
            else
            {
                action();
            }
        }

        public MainViewModel(
            IFreshRssService? freshRssService = null,
            INotificationService? notificationService = null,
            DispatcherQueue? dispatcherQueue = null,
            string? customDataFolder = null,
            IArticleImageResolver? articleImageResolver = null,
            ILocalStore? localStore = null)
        {
            _freshRssService = freshRssService ?? new FreshRssService();
            _notificationService = notificationService ?? new NotificationService();
            _articleImageResolver = articleImageResolver ?? new ArticleImageResolver();
            _localStore = localStore ?? new LocalStore(customDataFolder);
            
            try
            {
                _dispatcherQueue = dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            }
            catch
            {
                _dispatcherQueue = null;
            }

            _sentNotificationIds = _localStore.LoadSentNotifications();

            SyncCommand = new AsyncRelayCommand(SyncFeedsAsync);
            SaveSettingsCommand = new RelayCommand(SaveSettings);

            LoadSettings();
            LoadCache();
            InitializeLocalization();

            // Initialize startup configuration in background based on settings
            SafeFireAndForget.Run(() => StartupHelper.SetStartupAsync(AutoStartWithWindows, StartMinimizedInTray));

            // Perform initial login and sync
            SafeFireAndForget.Run(() => InitializeAppAsync());
        }

        public void RegisterTrayIconHelper(TrayIconHelper trayIconHelper)
        {
            _trayIconHelper = trayIconHelper;
            _trayIconHelper.UpdateUnreadCount(_unreadCount);
        }

        private void InitializeLocalization()
        {
            LocalizationManager.SetLanguage(Language);
            UpdateStatusTexts();
            LocalizationManager.LanguageChanged += (s, e) =>
            {
                EnqueueOnDispatcher(() =>
                {
                    OnPropertyChanged(string.Empty); // Refresh all bindings
                    UpdateStatusTexts();
                });
            };
        }

        private void UpdateStatusTexts()
        {
            if (string.IsNullOrEmpty(ConnectionStatusText))
            {
                ConnectionStatusText = LocalizationManager.Current.StatusDisconnected;
            }
        }

        private async Task InitializeAppAsync()
        {
            var token = CancelAndGetNewToken();

            SyncStatusText = LocalizationManager.Current.SyncingStatus;
            try
            {
                bool authed = await _freshRssService.AuthenticateAsync(ServerUrl, Username, ApiPassword, token);
                if (token.IsCancellationRequested) return;

                EnqueueOnDispatcher(() =>
                {
                    if (token.IsCancellationRequested) return;

                    if (authed)
                    {
                        ConnectionStatusText = LocalizationManager.Current.StatusConnected;
                    }
                    else
                    {
                        ConnectionStatusText = _freshRssService.LastConnectionFailed
                            ? LocalizationManager.Current.OfflineModeStatus
                            : LocalizationManager.Current.StatusDisconnected;
                    }
                });

                if (authed)
                {
                    await SyncPendingReadsAsync(token);
                    if (token.IsCancellationRequested) return;

                    await SyncFeedsInternalAsync(isFirstLoad: true, token);
                }
                else
                {
                    EnqueueOnDispatcher(() =>
                    {
                        if (token.IsCancellationRequested) return;

                        if (_freshRssService.LastConnectionFailed)
                        {
                            SyncStatusText = LocalizationManager.Current.OfflineModeStatus;
                        }
                        else
                        {
                            SyncStatusText = string.Format(LocalizationManager.Current.SyncError, "Invalid Credentials");
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Silent exit on cancellation
            }
            catch (Exception ex)
            {
                SyncStatusText = string.Format(LocalizationManager.Current.SyncError, ex.Message);
            }

            // Start background synchronization timer
            SetupSyncTimer();
        }

        private void SetupSyncTimer()
        {
            _syncTimerCts?.Cancel();
            _syncTimerCts = new CancellationTokenSource();
            var token = _syncTimerCts.Token;
            var intervalSpan = TimeSpan.FromMinutes(Math.Max(1, SyncInterval));

            SafeFireAndForget.Run(() => Task.Run(async () =>
            {
                using var periodicTimer = new PeriodicTimer(intervalSpan);
                while (await periodicTimer.WaitForNextTickAsync(token))
                {
                    try
                    {
                        var syncToken = CancelAndGetNewToken();
                        await SyncFeedsInternalAsync(isFirstLoad: false, syncToken);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background sync failed: {ex.Message}");
                    }
                }
            }, token));
        }

        public async Task SyncFeedsAsync()
        {
            var token = CancelAndGetNewToken();

            IsSyncing = true;
            SyncStatusText = LocalizationManager.Current.SyncingStatus;

            try
            {
                // Ensure authenticated
                bool authed = await _freshRssService.AuthenticateAsync(ServerUrl, Username, ApiPassword, token);
                if (token.IsCancellationRequested) return;

                EnqueueOnDispatcher(() =>
                {
                    if (token.IsCancellationRequested) return;

                    if (authed)
                    {
                        ConnectionStatusText = LocalizationManager.Current.StatusConnected;
                    }
                    else
                    {
                        ConnectionStatusText = _freshRssService.LastConnectionFailed
                            ? LocalizationManager.Current.OfflineModeStatus
                            : LocalizationManager.Current.StatusDisconnected;
                    }
                });

                if (authed)
                {
                    await SyncPendingReadsAsync(token);
                    if (token.IsCancellationRequested) return;

                    await SyncFeedsInternalAsync(isFirstLoad: false, token);
                }
                else
                {
                    IsSyncing = false;
                    EnqueueOnDispatcher(() =>
                    {
                        if (token.IsCancellationRequested) return;

                        if (_freshRssService.LastConnectionFailed)
                        {
                            SyncStatusText = LocalizationManager.Current.OfflineModeStatus;
                        }
                        else
                        {
                            SyncStatusText = string.Format(LocalizationManager.Current.SyncError, "Authentication failed");
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Silent exit on cancellation
                System.Diagnostics.Debug.WriteLine("Sync aborted silently due to cancellation.");
            }
            catch (Exception ex)
            {
                IsSyncing = false;
                SyncStatusText = string.Format(LocalizationManager.Current.SyncError, ex.Message);
            }
        }

        private async Task SyncFeedsInternalAsync(bool isFirstLoad, CancellationToken token = default)
        {
            EnqueueOnDispatcher(() => IsSyncing = true);
            string? activeStreamId = ActiveStreamId;

            // 1. Fetch categories/feeds and active stream articles in parallel
            var categoriesAndFeedsTask = _freshRssService.FetchSubscriptionsAndUnreadCountsAsync(token);
            var articlesTask = _freshRssService.FetchArticlesAsync(activeStreamId, false, MaxReadArticles, null, token);
            var notificationArticlesTask = activeStreamId == null
                ? articlesTask
                : _freshRssService.FetchArticlesAsync(null, false, MaxReadArticles, null, token);

            await Task.WhenAll(categoriesAndFeedsTask, articlesTask, notificationArticlesTask);
            if (token.IsCancellationRequested) return;

            var (fetchedCategories, fetchedFeeds) = await categoriesAndFeedsTask;
            var fetchedArticles = await articlesTask;
            var notificationArticles = await notificationArticlesTask;

            if (activeStreamId == "uncategorized")
            {
                var uncategorized = fetchedCategories.FirstOrDefault(category => category.Id == "uncategorized");
                var feedIds = uncategorized?.Feeds.Select(feed => feed.Id).ToHashSet() ?? new HashSet<string>();
                fetchedArticles = fetchedArticles.Where(article => feedIds.Contains(article.FeedId)).ToList();
            }

            ApplyKnownImages(fetchedArticles);
            ApplyKnownImages(notificationArticles);

            foreach (var article in fetchedArticles)
            {
                AttachCommands(article);
            }

            if (token.IsCancellationRequested) return;

            _localStore.SaveCache(fetchedCategories, fetchedFeeds, fetchedArticles, activeStreamId ?? "all");
            if (activeStreamId != null)
            {
                _localStore.SaveCache(fetchedCategories, fetchedFeeds, notificationArticles, "all");
            }

            EnqueueOnDispatcher(() =>
            {
                if (token.IsCancellationRequested) return;

                // Smart categories update
                bool needsFullRebuild = Categories.Count != fetchedCategories.Count ||
                                       !Categories.Select(c => c.Id).SequenceEqual(fetchedCategories.Select(c => c.Id));

                if (!needsFullRebuild)
                {
                    // Check if any category has different feeds
                    foreach (var fetchedCat in fetchedCategories)
                    {
                        var existingCat = Categories.FirstOrDefault(c => c.Id == fetchedCat.Id);
                        if (existingCat == null || 
                            existingCat.Feeds.Count != fetchedCat.Feeds.Count ||
                            !existingCat.Feeds.Select(f => f.Id).SequenceEqual(fetchedCat.Feeds.Select(f => f.Id)))
                        {
                            needsFullRebuild = true;
                            break;
                        }
                    }
                }

                if (needsFullRebuild)
                {
                    Categories.Clear();
                    foreach (var cat in fetchedCategories)
                    {
                        Categories.Add(cat);
                    }
                    NavigationStructureChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    // Update unread counts in place to preserve UI expansion/selection states
                    foreach (var fetchedCat in fetchedCategories)
                    {
                        var existingCat = Categories.First(c => c.Id == fetchedCat.Id);
                        existingCat.UnreadCount = fetchedCat.UnreadCount;
                        foreach (var fetchedFeed in fetchedCat.Feeds)
                        {
                            var existingFeed = existingCat.Feeds.First(f => f.Id == fetchedFeed.Id);
                            existingFeed.UnreadCount = fetchedFeed.UnreadCount;
                        }
                    }
                }

                // Detect new articles for notification
                if (!isFirstLoad)
                {
                    bool notificationsAdded = false;
                    foreach (var article in notificationArticles)
                    {
                        // If it is unread and we have NOT sent a notification for it yet
                        if (!article.IsRead && !_sentNotificationIds.Contains(article.Id))
                        {
                            _sentNotificationIds.Add(article.Id);
                            notificationsAdded = true;
                            _notificationService.SendArticleNotification(article.Id, article.FeedTitle, article.Title, article.ImageUrl);
                        }
                    }
                    if (notificationsAdded)
                    {
                        _localStore.SaveSentNotifications(_sentNotificationIds);
                    }
                }
                else
                {
                    // On first load, populate the sent notification list with existing unread articles
                    // so we don't alert for them upon subsequent syncs
                    bool notificationsAdded = false;
                    foreach (var article in notificationArticles)
                    {
                        if (!_sentNotificationIds.Contains(article.Id))
                        {
                            _sentNotificationIds.Add(article.Id);
                            notificationsAdded = true;
                        }
                    }
                    if (notificationsAdded)
                    {
                        _localStore.SaveSentNotifications(_sentNotificationIds);
                    }
                }

                _currentAllArticles.Clear();
                _currentAllArticles.AddRange(fetchedArticles);

                ApplyLocalSearch();

                // Total global unread is the sum of all feed unread counts
                int totalUnread = fetchedFeeds.Sum(f => f.UnreadCount);
                UnreadCount = totalUnread;

                IsSyncing = false;
                SyncStatusText = LocalizationManager.Current.SyncSuccess;

                StartImageEnrichment();
            });
        }

        /// <summary>
        /// Restarts the image enrichment pass for the currently loaded articles, cancelling any pass still in flight.
        /// Must be called from the UI thread so the article snapshot is taken safely.
        /// </summary>
        private void StartImageEnrichment()
        {
            if (!FetchMissingImagesFromWeb)
            {
                DebugLog.Write(ImageLogTag, "pass non avviato: impostazione disattivata");
                return;
            }

            _imageEnrichmentCts?.Cancel();
            _imageEnrichmentCts?.Dispose();
            _imageEnrichmentCts = new CancellationTokenSource();
            var token = _imageEnrichmentCts.Token;
            SafeFireAndForget.Run(() => EnrichMissingArticleImagesAsync(token));
        }

        /// <summary>
        /// Resolves images for articles whose feed entry carries none, by scraping the social/meta tags
        /// of the linked page. Runs in the background so the article list is never blocked.
        /// </summary>
        private async Task EnrichMissingArticleImagesAsync(CancellationToken token)
        {
            if (!FetchMissingImagesFromWeb || token.IsCancellationRequested)
            {
                return;
            }

            // Snapshot on the caller (UI) thread before the first await
            var targets = _currentAllArticles
                .Where(article => string.IsNullOrEmpty(article.ImageUrl) && !string.IsNullOrEmpty(article.Link))
                .Take(MaxImageEnrichmentPerSync)
                .ToList();

            DebugLog.Write(ImageLogTag, $"articoli caricati: {_currentAllArticles.Count}, senza immagine: {_currentAllArticles.Count(article => string.IsNullOrEmpty(article.ImageUrl))}, da elaborare: {targets.Count}");

            if (targets.Count == 0)
            {
                return;
            }

            var resolvedImages = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);

            using var passCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            passCts.CancelAfter(TimeSpan.FromSeconds(ImageEnrichmentPassTimeoutSeconds));
            using var throttle = new SemaphoreSlim(ImageEnrichmentConcurrency);

            try
            {
                await Task.WhenAll(targets.Select(async article =>
                {
                    await throttle.WaitAsync(passCts.Token);
                    try
                    {
                        var imageUrl = await _articleImageResolver.ResolveAsync(article.Link, passCts.Token);
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            resolvedImages[article.Id] = imageUrl;
                            _knownImagesByArticleId[article.Id] = imageUrl;
                            EnqueueOnDispatcher(() => ApplyResolvedImage(article.Id, imageUrl));
                        }
                        else
                        {
                            DebugLog.Write(ImageLogTag, $"nessuna immagine per \"{article.Title}\" ({article.Link})");
                        }
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }));
            }
            catch (OperationCanceledException)
            {
                DebugLog.Write(ImageLogTag, "pass interrotto (budget scaduto o sync annullata)");
            }

            DebugLog.Write(ImageLogTag, $"pass concluso: {resolvedImages.Count}/{targets.Count} immagini risolte");

            if (!resolvedImages.IsEmpty && !token.IsCancellationRequested)
            {
                _localStore.UpdateArticleImagesInCache(resolvedImages);
            }
        }

        /// <summary>
        /// Restores images already known for these articles, so a fresh server fetch does not drop them.
        /// </summary>
        private void ApplyKnownImages(List<RssArticle> articles)
        {
            int restored = 0;
            foreach (var article in articles)
            {
                if (string.IsNullOrEmpty(article.ImageUrl) &&
                    _knownImagesByArticleId.TryGetValue(article.Id, out var knownImage))
                {
                    article.ImageUrl = knownImage;
                    restored++;
                }
                else if (!string.IsNullOrEmpty(article.ImageUrl))
                {
                    _knownImagesByArticleId[article.Id] = article.ImageUrl;
                }
            }

            if (restored > 0)
            {
                DebugLog.Write(ImageLogTag, $"ripristinate {restored} immagini già note sugli articoli scaricati");
            }
        }

        private void ApplyResolvedImage(string articleId, string imageUrl)
        {
            var displayed = Articles.FirstOrDefault(article => article.Id == articleId);
            if (displayed != null)
            {
                displayed.ImageUrl = imageUrl;
            }

            var source = _currentAllArticles.FirstOrDefault(article => article.Id == articleId);
            if (source != null && source != displayed)
            {
                source.ImageUrl = imageUrl;
            }

            DebugLog.Write(ImageLogTag, $"applico immagine a {articleId} (in lista: {displayed != null}, in sorgente: {source != null}): {imageUrl}");
        }

        public async Task MarkArticleAsReadAsync(RssArticle article)
        {
            if (article.IsRead) return;

            // Mark as read locally immediately for smooth UI transition
            article.IsRead = true;
            
            // Decrement feed and category unread counts locally for real-time sidebar badge updates
            UpdateLocalUnreadCounts(article.FeedId);

            // Add to pending reads list and save
            _localStore.SetPendingReadState(article.Id, true);

            // Update local cache so that this article's read status is saved offline
            _localStore.UpdateArticleReadStatusInCache(article.Id, true);

            // Dismiss Action Center notification if active
            _notificationService.DismissNotification(article.Id);

            // Call API
            bool success = await _freshRssService.MarkAsReadAsync(article.Id);
            
            if (success)
            {
                // If api succeeded, remove from pending reads
                _localStore.RemovePendingReadState(article.Id);
            }
        }

        public async Task MarkArticleAsUnreadAsync(RssArticle article)
        {
            if (!article.IsRead) return;

            // Mark as unread locally immediately for smooth UI transition
            article.IsRead = false;
            
            // Increment feed and category unread counts locally for real-time sidebar badge updates
            UpdateLocalUnreadCountsForUnread(article.FeedId);

            // Update local cache so that this article's read status is saved offline
            _localStore.UpdateArticleReadStatusInCache(article.Id, false);

            _localStore.SetPendingReadState(article.Id, false);

            // Call API
            if (await _freshRssService.MarkAsUnreadAsync(article.Id))
            {
                _localStore.RemovePendingReadState(article.Id);
            }
        }

        public async Task MarkAsReadAndOpenBrowserAsync(RssArticle article)
        {
            if (!string.IsNullOrEmpty(article.Link))
            {
                try
                {
                    if (WebUri.TryCreate(article.Link, out var uri))
                    {
                        await Windows.System.Launcher.LaunchUriAsync(uri);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to launch browser: {ex.Message}");
                }
            }
            await MarkArticleAsReadAsync(article);
        }

        private void SaveSettingsToFile()
        {
            var settings = new AppSettings
            {
                ServerUrl = ServerUrl,
                Username = Username,
                SyncInterval = SyncInterval,
                ShowUnreadOnly = ShowUnreadOnly,
                ArticleFilter = ArticleFilter,
                MaxReadArticles = MaxReadArticles,
                Language = Language,
                UseGridLayout = UseGridLayout,
                OpenLinksInBrowser = OpenLinksInBrowser,
                AutoStartWithWindows = AutoStartWithWindows,
                StartMinimizedInTray = StartMinimizedInTray
            };

            _localStore.SaveSettings(settings, ApiPassword);
        }

        public void SaveAndApplySettings(bool reconnect = false)
        {
            try
            {
                SaveSettingsToFile();

                // Update system startup configuration
                SafeFireAndForget.Run(() => StartupHelper.SetStartupAsync(AutoStartWithWindows, StartMinimizedInTray));

                // Switch language
                LocalizationManager.SetLanguage(Language);

                // Re-apply background timer
                SetupSyncTimer();

                if (reconnect)
                {
                    // Authenticate and sync now
                    SafeFireAndForget.Run(() => InitializeAppAsync());
                }
                else
                {
                    SyncStatusText = LocalizationManager.Current.SettingsSaved;
                }
            }
            catch (Exception ex)
            {
                SyncStatusText = string.Format(LocalizationManager.Current.SyncError, ex.Message);
            }
        }

        private void SaveSettings()
        {
            SaveAndApplySettings(reconnect: true);
        }

        private void LoadSettings()
        {
            var (settings, legacyPassword) = _localStore.LoadSettings();
            if (settings == null) return;

            _serverUrl = settings.ServerUrl;
            _username = settings.Username;
            _apiPassword = _localStore.LoadCredential(settings.Username) ?? legacyPassword;
            _syncInterval = settings.SyncInterval;
            _articleFilter = settings.ArticleFilter ?? (settings.ShowUnreadOnly ? "Unread" : "All");
            _showUnreadOnly = _articleFilter == "Unread";
            _maxReadArticles = settings.MaxReadArticles;
            _language = settings.Language;
            _useGridLayout = settings.UseGridLayout;
            _openLinksInBrowser = settings.OpenLinksInBrowser;
            _fetchMissingImagesFromWeb = settings.FetchMissingImagesFromWeb;
            _autoStartWithWindows = settings.AutoStartWithWindows;
            _startMinimizedInTray = settings.StartMinimizedInTray;

            // Migrate a legacy plaintext password into the credential locker
            if (_localStore.CredentialLockerEnabled && !string.IsNullOrEmpty(legacyPassword))
            {
                SaveSettingsToFile();
            }
        }

        public void Dispose()
        {
            _syncTimerCts?.Cancel();
            _syncCts?.Cancel();
            _imageEnrichmentCts?.Cancel();
            GC.SuppressFinalize(this);
        }

        private void LoadCache()
        {
            var cache = _localStore.LoadCache();
            if (cache == null) return;

            Categories.Clear();
            foreach (var cat in cache.Categories)
            {
                Categories.Add(cat);
            }

            UnreadCount = cache.Feeds.Sum(f => f.UnreadCount);

            string activeKey = ActiveStreamId ?? "all";
            var articles = GetCachedArticles(cache, activeKey);

            ApplyKnownImages(articles);
            _currentAllArticles.Clear();
            _currentAllArticles.AddRange(articles);
            ApplyLocalSearch();
            StartImageEnrichment();

            NavigationStructureChanged?.Invoke(this, EventArgs.Empty);
        }

        private List<RssArticle> GetCachedArticles(OfflineCache cache, string activeKey)
        {
            List<RssArticle>? articles = null;
            
            // 1. Try to get cached articles specifically for this stream ID
            if (cache.ArticlesByStream != null && cache.ArticlesByStream.TryGetValue(activeKey, out var streamArticles))
            {
                articles = streamArticles;
            }
            
            // 2. If no specific cache, fallback to filtering the global "all" list locally
            if ((articles == null || articles.Count == 0) && cache.ArticlesByStream != null && cache.ArticlesByStream.TryGetValue("all", out var allArticles))
            {
                if (activeKey == "all")
                {
                    articles = allArticles;
                }
                else if (activeKey == "uncategorized")
                {
                    var uncategorizedCategory = Categories.FirstOrDefault(c => c.Id == "uncategorized");
                    if (uncategorizedCategory != null)
                    {
                        var feedIds = uncategorizedCategory.Feeds.Select(f => f.Id).ToHashSet();
                        articles = allArticles.Where(a => feedIds.Contains(a.FeedId)).ToList();
                    }
                }
                else if (activeKey.StartsWith("feed/"))
                {
                    articles = allArticles.Where(a => a.FeedId == activeKey).ToList();
                }
                else
                {
                    var category = Categories.FirstOrDefault(c => c.Id == activeKey);
                    if (category != null)
                    {
                        var feedIds = category.Feeds.Select(f => f.Id).ToHashSet();
                        articles = allArticles.Where(a => feedIds.Contains(a.FeedId)).ToList();
                    }
                }
            }
            
            return articles ?? new List<RssArticle>();
        }

        private void LoadCachedArticlesForActiveStream()
        {
            var cache = _localStore.LoadCache();
            if (cache == null)
            {
                Articles.Clear();
                return;
            }

            string activeKey = ActiveStreamId ?? "all";
            var articles = GetCachedArticles(cache, activeKey);

            ApplyKnownImages(articles);
            _currentAllArticles.Clear();
            _currentAllArticles.AddRange(articles);
            ApplyLocalSearch();
            StartImageEnrichment();
        }

        private void UpdateLocalUnreadCounts(string feedId)
        {
            EnqueueOnDispatcher(() =>
            {
                foreach (var cat in Categories)
                {
                    var feed = cat.Feeds.FirstOrDefault(f => f.Id == feedId);
                    if (feed != null)
                    {
                        if (feed.UnreadCount > 0) feed.UnreadCount--;
                        if (cat.UnreadCount > 0) cat.UnreadCount--;
                        break;
                    }
                }

                if (UnreadCount > 0)
                {
                    UnreadCount--;
                }
            });
        }

        private void UpdateLocalUnreadCountsForUnread(string feedId)
        {
            EnqueueOnDispatcher(() =>
            {
                foreach (var cat in Categories)
                {
                    var feed = cat.Feeds.FirstOrDefault(f => f.Id == feedId);
                    if (feed != null)
                    {
                        feed.UnreadCount++;
                        cat.UnreadCount++;
                        break;
                    }
                }

                UnreadCount++;
            });
        }

        public async Task MarkAllAsReadInActiveStreamAsync()
        {
            var unreadArticles = Articles.Where(a => !a.IsRead).ToList();
            if (unreadArticles.Count == 0) return;

            // 1. Mark all as read locally immediately for smooth UI transition
            foreach (var article in unreadArticles)
            {
                article.IsRead = true;
                _localStore.UpdateArticleReadStatusInCache(article.Id, true);
                _localStore.SetPendingReadState(article.Id, true);
                _notificationService.DismissNotification(article.Id);
            }

            // 2. Clear counts locally
            if (string.IsNullOrEmpty(ActiveStreamId))
            {
                EnqueueOnDispatcher(() =>
                {
                    foreach (var cat in Categories)
                    {
                        cat.UnreadCount = 0;
                        foreach (var feed in cat.Feeds)
                        {
                            feed.UnreadCount = 0;
                        }
                    }
                    UnreadCount = 0;
                });
            }
            else if (ActiveStreamId.StartsWith("feed/"))
            {
                int markedCount = unreadArticles.Count;
                EnqueueOnDispatcher(() =>
                {
                    foreach (var cat in Categories)
                    {
                        var feed = cat.Feeds.FirstOrDefault(f => f.Id == ActiveStreamId);
                        if (feed != null)
                        {
                            feed.UnreadCount = Math.Max(0, feed.UnreadCount - markedCount);
                            cat.UnreadCount = Math.Max(0, cat.UnreadCount - markedCount);
                            break;
                        }
                    }
                    UnreadCount = Math.Max(0, UnreadCount - markedCount);
                });
            }
            else
            {
                int markedCount = unreadArticles.Count;
                EnqueueOnDispatcher(() =>
                {
                    var category = Categories.FirstOrDefault(c => c.Id == ActiveStreamId);
                    if (category != null)
                    {
                        category.UnreadCount = Math.Max(0, category.UnreadCount - markedCount);
                        foreach (var feed in category.Feeds)
                        {
                            var feedUnreadCount = unreadArticles.Count(a => a.FeedId == feed.Id);
                            feed.UnreadCount = Math.Max(0, feed.UnreadCount - feedUnreadCount);
                        }
                    }
                    UnreadCount = Math.Max(0, UnreadCount - markedCount);
                });
            }

            // 3. Call API for each article individually (more reliable than bulk API)
            var tasks = unreadArticles.Select(async article =>
            {
                bool success = await _freshRssService.MarkAsReadAsync(article.Id);
                if (success)
                {
                    _localStore.RemovePendingReadState(article.Id);
                }
            }).ToList();

            await Task.WhenAll(tasks);

            bool bulkSuccess;
            if (ActiveStreamId == "uncategorized")
            {
                var streams = Categories
                    .FirstOrDefault(category => category.Id == "uncategorized")?
                    .Feeds.Select(feed => feed.Id) ?? [];
                var results = await Task.WhenAll(streams.Select(stream => _freshRssService.MarkAllAsReadAsync(stream)));
                bulkSuccess = results.All(result => result);
            }
            else
            {
                bulkSuccess = await _freshRssService.MarkAllAsReadAsync(ActiveStreamId);
            }

            if (bulkSuccess && !string.IsNullOrEmpty(ActiveStreamId))
            {
                EnqueueOnDispatcher(() =>
                {
                    if (ActiveStreamId.StartsWith("feed/"))
                    {
                        var category = Categories.FirstOrDefault(item => item.Feeds.Any(feed => feed.Id == ActiveStreamId));
                        var feed = category?.Feeds.FirstOrDefault(item => item.Id == ActiveStreamId);
                        if (feed != null)
                        {
                            UnreadCount = Math.Max(0, UnreadCount - feed.UnreadCount);
                            category!.UnreadCount = Math.Max(0, category.UnreadCount - feed.UnreadCount);
                            feed.UnreadCount = 0;
                        }
                    }
                    else
                    {
                        var category = Categories.FirstOrDefault(item => item.Id == ActiveStreamId);
                        if (category != null)
                        {
                            UnreadCount = Math.Max(0, UnreadCount - category.UnreadCount);
                            category.UnreadCount = 0;
                            foreach (var feed in category.Feeds) feed.UnreadCount = 0;
                        }
                    }
                });
            }
        }

        public async Task MarkSelectedAsReadAsync()
        {
            var articlesToMark = SelectedArticles.Where(a => !a.IsRead).ToList();
            if (articlesToMark.Count == 0)
            {
                IsMultiSelectMode = false;
                return;
            }

            // 1. Mark locally
            foreach (var article in articlesToMark)
            {
                article.IsRead = true;
                _localStore.UpdateArticleReadStatusInCache(article.Id, true);
                _localStore.SetPendingReadState(article.Id, true);
                UpdateLocalUnreadCounts(article.FeedId);
                _notificationService.DismissNotification(article.Id);
            }

            // 2. Call API for each in parallel
            var tasks = articlesToMark.Select(async article =>
            {
                bool success = await _freshRssService.MarkAsReadAsync(article.Id);
                if (success)
                {
                    _localStore.RemovePendingReadState(article.Id);
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // 3. Turn off multi-select mode
            IsMultiSelectMode = false;
        }

        public async Task OpenSelectedInBrowserAsync()
        {
            var articlesToOpen = SelectedArticles.ToList();
            if (articlesToOpen.Count == 0)
            {
                IsMultiSelectMode = false;
                return;
            }

            // 1. Open in browser
            foreach (var article in articlesToOpen)
            {
                if (!string.IsNullOrEmpty(article.Link))
                {
                    try
                    {
                        if (WebUri.TryCreate(article.Link, out var uri))
                        {
                            await Windows.System.Launcher.LaunchUriAsync(uri);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to open {article.Link}: {ex.Message}");
                    }
                }
            }

            // 2. Mark as read
            var articlesToMark = articlesToOpen.Where(a => !a.IsRead).ToList();
            if (articlesToMark.Count > 0)
            {
                foreach (var article in articlesToMark)
                {
                    article.IsRead = true;
                    _localStore.UpdateArticleReadStatusInCache(article.Id, true);
                    _localStore.SetPendingReadState(article.Id, true);
                    UpdateLocalUnreadCounts(article.FeedId);
                    _notificationService.DismissNotification(article.Id);
                }

                var tasks = articlesToMark.Select(async article =>
                {
                    bool success = await _freshRssService.MarkAsReadAsync(article.Id);
                    if (success)
                    {
                        _localStore.RemovePendingReadState(article.Id);
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }

            // 3. Turn off multi-select mode
            IsMultiSelectMode = false;
        }

        private async Task SyncPendingReadsAsync(CancellationToken cancellationToken = default)
        {
            var pending = _localStore.LoadPendingReads();
            if (pending.Count == 0) return;

            EnqueueOnDispatcher(() =>
            {
                SyncStatusText = LocalizationManager.Current.SyncPendingReadsStatus;
            });

            // Run pending mark-as-read requests in parallel
            var tasks = pending.Select(async entry =>
            {
                try
                {
                    bool success = entry.Value
                        ? await _freshRssService.MarkAsReadAsync(entry.Key, cancellationToken)
                        : await _freshRssService.MarkAsUnreadAsync(entry.Key, cancellationToken);
                    return (ArticleId: entry.Key, Success: success);
                }
                catch
                {
                    return (ArticleId: entry.Key, Success: false);
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            var syncedIds = results.Where(r => r.Success).Select(r => r.ArticleId).ToList();

            if (syncedIds.Count > 0)
            {
                foreach (var id in syncedIds)
                {
                    pending.Remove(id);
                }
                _localStore.SavePendingReads(pending);
            }
        }
    }
}
