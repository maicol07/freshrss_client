using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FreshRssClient.Services;
using FreshRssClient.Helpers;

namespace FreshRssClient.ViewModels
{
    public class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IFreshRssService _freshRssService;
        private readonly INotificationService _notificationService;
        private readonly DispatcherQueue? _dispatcherQueue;
        private CancellationTokenSource? _syncCts;
        private CancellationTokenSource? _syncTimerCts;
        private readonly object _syncLock = new();
        private TrayIconHelper? _trayIconHelper;
        private readonly string _sentNotificationsFilePath;
        private HashSet<string> _sentNotificationIds = new();

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
        private readonly string _settingsFilePath;
        private readonly string _cacheFilePath;
        private readonly string _pendingReadsFilePath;

        // Settings Form Properties
        private string _serverUrl = string.Empty;
        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                if (SetProperty(ref _serverUrl, value))
                {
                    SaveAndApplySettings(reconnect: true);
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
                    SaveAndApplySettings(reconnect: true);
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

        private bool _enableOpenGraph = false;
        public bool EnableOpenGraph
        {
            get => _enableOpenGraph;
            set
            {
                if (SetProperty(ref _enableOpenGraph, value))
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
            List<RssArticle> filtered;

            if (string.IsNullOrEmpty(query))
            {
                filtered = _currentAllArticles;
            }
            else
            {
                filtered = _currentAllArticles.Where(a => 
                    a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (a.Summary != null && a.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (a.Content != null && a.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (a.FeedTitle != null && a.FeedTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            UpdateDisplayedArticles(filtered);
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
                        existing.ImageUrl = fetched.ImageUrl;
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
                            Articles[i].ImageUrl = fetched.ImageUrl;
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

        public void SelectAllArticles()
        {
            SelectedArticle = null;
            SelectedCategory = null;
            SelectedFeed = null;
            ActiveStreamId = null;
            LoadCachedArticlesForActiveStream();
            if (!IsSyncing)
            {
                SafeFireAndForget.Run(() => SyncFeedsAsync());
            }
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
            string? customDataFolder = null)
        {
            _freshRssService = freshRssService ?? new FreshRssService();
            _notificationService = notificationService ?? new NotificationService();
            
            try
            {
                _dispatcherQueue = dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            }
            catch
            {
                _dispatcherQueue = null;
            }

            // Set settings path inside LocalAppData or custom data folder
            var localFolder = customDataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreshRssClient");
            Directory.CreateDirectory(localFolder);
            _settingsFilePath = Path.Combine(localFolder, "settings.json");
            _cacheFilePath = Path.Combine(localFolder, "cache.json");
            _pendingReadsFilePath = Path.Combine(localFolder, "pending_reads.json");
            _sentNotificationsFilePath = Path.Combine(localFolder, "sent_notifications.json");

            _sentNotificationIds = LoadSentNotifications();

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
            ConnectionStatusText = _freshRssService.MarkAsReadAsync("test").Status == TaskStatus.Created 
                ? LocalizationManager.Current.StatusDisconnected 
                : LocalizationManager.Current.StatusConnected;
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

            // 1. Fetch categories/feeds and active stream articles in parallel
            var categoriesAndFeedsTask = _freshRssService.FetchSubscriptionsAndUnreadCountsAsync(token);
            var articlesTask = _freshRssService.FetchArticlesAsync(ActiveStreamId, ShowUnreadOnly, MaxReadArticles, EnableOpenGraph, SearchQuery, token);

            await Task.WhenAll(categoriesAndFeedsTask, articlesTask);
            if (token.IsCancellationRequested) return;

            var (fetchedCategories, fetchedFeeds) = await categoriesAndFeedsTask;
            var fetchedArticles = await articlesTask;

            // Apply local ArticleFilter
            if (ArticleFilter == "Read")
            {
                fetchedArticles = fetchedArticles.Where(a => a.IsRead).ToList();
            }
            else if (ArticleFilter == "Unread")
            {
                fetchedArticles = fetchedArticles.Where(a => !a.IsRead).ToList();
            }

            foreach (var article in fetchedArticles)
            {
                AttachCommands(article);
            }

            if (token.IsCancellationRequested) return;

            // Save cache in a background thread to prevent UI stutter
            SafeFireAndForget.Run(() => Task.Run(() => SaveCache(fetchedCategories, fetchedFeeds, fetchedArticles)));

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

                // If active stream is uncategorized, filter them locally
                if (ActiveStreamId == "uncategorized")
                {
                    var uncategorizedCategory = Categories.FirstOrDefault(c => c.Id == "uncategorized");
                    if (uncategorizedCategory != null)
                    {
                        var feedIds = uncategorizedCategory.Feeds.Select(f => f.Id).ToHashSet();
                        fetchedArticles = fetchedArticles.Where(a => feedIds.Contains(a.FeedId)).ToList();
                    }
                }

                // Detect new articles for notification
                if (!isFirstLoad)
                {
                    bool notificationsAdded = false;
                    foreach (var article in fetchedArticles)
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
                        SaveSentNotifications();
                    }
                }
                else
                {
                    // On first load, populate the sent notification list with existing unread articles
                    // so we don't alert for them upon subsequent syncs
                    bool notificationsAdded = false;
                    foreach (var article in fetchedArticles)
                    {
                        if (!_sentNotificationIds.Contains(article.Id))
                        {
                            _sentNotificationIds.Add(article.Id);
                            notificationsAdded = true;
                        }
                    }
                    if (notificationsAdded)
                    {
                        SaveSentNotifications();
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
            });
        }

        private async Task MarkArticleAsReadAsync(RssArticle article)
        {
            if (article.IsRead) return;

            // Mark as read locally immediately for smooth UI transition
            article.IsRead = true;
            
            // Decrement feed and category unread counts locally for real-time sidebar badge updates
            UpdateLocalUnreadCounts(article.FeedId);

            // Add to pending reads list and save
            AddPendingRead(article.Id);

            // Update local cache so that this article's read status is saved offline
            UpdateArticleReadStatusInCache(article.Id, true);

            // Dismiss Action Center notification if active
            _notificationService.DismissNotification(article.Id);

            // Call API
            bool success = await _freshRssService.MarkAsReadAsync(article.Id);
            
            if (success)
            {
                // If api succeeded, remove from pending reads
                RemovePendingRead(article.Id);
            }
        }

        public async Task MarkAsReadAndOpenBrowserAsync(RssArticle article)
        {
            if (!string.IsNullOrEmpty(article.Link))
            {
                try
                {
                    var uri = new Uri(article.Link);
                    await Windows.System.Launcher.LaunchUriAsync(uri);
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
            try
            {
                var settings = new AppSettings
                {
                    ServerUrl = ServerUrl,
                    Username = Username,
                    ApiPassword = ApiPassword,
                    SyncInterval = SyncInterval,
                    EnableOpenGraph = EnableOpenGraph,
                    ShowUnreadOnly = ShowUnreadOnly,
                    ArticleFilter = ArticleFilter,
                    MaxReadArticles = MaxReadArticles,
                    Language = Language,
                    UseGridLayout = UseGridLayout,
                    OpenLinksInBrowser = OpenLinksInBrowser,
                    AutoStartWithWindows = AutoStartWithWindows,
                    StartMinimizedInTray = StartMinimizedInTray
                };

                var json = JsonSerializer.Serialize(settings);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
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
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        _serverUrl = settings.ServerUrl;
                        _username = settings.Username;
                        _apiPassword = settings.ApiPassword;
                        _syncInterval = settings.SyncInterval;
                        _enableOpenGraph = settings.EnableOpenGraph;
                        _articleFilter = settings.ArticleFilter ?? (settings.ShowUnreadOnly ? "Unread" : "All");
                        _showUnreadOnly = _articleFilter == "Unread";
                        _maxReadArticles = settings.MaxReadArticles;
                        _language = settings.Language;
                        _useGridLayout = settings.UseGridLayout;
                        _openLinksInBrowser = settings.OpenLinksInBrowser;
                        _autoStartWithWindows = settings.AutoStartWithWindows;
                        _startMinimizedInTray = settings.StartMinimizedInTray;
                    }
                }
            }
            catch
            {
                // Fallback to defaults
            }
        }

        public void Dispose()
        {
            _syncTimerCts?.Cancel();
            _syncCts?.Cancel();
            GC.SuppressFinalize(this);
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var cache = JsonSerializer.Deserialize<OfflineCache>(json, options);
                    
                    if (cache != null)
                    {
                        Categories.Clear();
                        foreach (var cat in cache.Categories)
                        {
                            Categories.Add(cat);
                        }

                        int totalUnread = cache.Feeds.Sum(f => f.UnreadCount);
                        UnreadCount = totalUnread;

                        string activeKey = ActiveStreamId ?? "all";
                        var articles = GetCachedArticles(cache, activeKey);
                        
                        // Apply ArticleFilter locally
                        if (ArticleFilter == "Read")
                        {
                            articles = articles.Where(a => a.IsRead).ToList();
                        }
                        else if (ArticleFilter == "Unread")
                        {
                            articles = articles.Where(a => !a.IsRead).ToList();
                        }

                        _currentAllArticles.Clear();
                        _currentAllArticles.AddRange(articles);
                        ApplyLocalSearch();

                        NavigationStructureChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load cache: {ex.Message}");
            }
        }

        private void SaveCache(List<RssCategory> fetchedCategories, List<RssFeed> fetchedFeeds, List<RssArticle> fetchedArticles)
        {
            try
            {
                OfflineCache cache;
                
                if (File.Exists(_cacheFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_cacheFilePath);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        cache = JsonSerializer.Deserialize<OfflineCache>(json, options) ?? new OfflineCache();
                    }
                    catch
                    {
                        cache = new OfflineCache();
                    }
                }
                else
                {
                    cache = new OfflineCache();
                }

                cache.Categories = fetchedCategories;
                cache.Feeds = fetchedFeeds;

                string activeKey = ActiveStreamId ?? "all";
                cache.ArticlesByStream ??= new Dictionary<string, List<RssArticle>>();
                cache.ArticlesByStream[activeKey] = fetchedArticles;

                var serializedJson = JsonSerializer.Serialize(cache);
                File.WriteAllText(_cacheFilePath, serializedJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save cache: {ex.Message}");
            }
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
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var cache = JsonSerializer.Deserialize<OfflineCache>(json, options);
                    
                    if (cache != null)
                    {
                        string activeKey = ActiveStreamId ?? "all";
                        var articles = GetCachedArticles(cache, activeKey);
                        
                        // Apply ArticleFilter locally
                        if (ArticleFilter == "Read")
                        {
                            articles = articles.Where(a => a.IsRead).ToList();
                        }
                        else if (ArticleFilter == "Unread")
                        {
                            articles = articles.Where(a => !a.IsRead).ToList();
                        }

                        _currentAllArticles.Clear();
                        _currentAllArticles.AddRange(articles);
                        ApplyLocalSearch();
                    }
                    else
                    {
                        Articles.Clear();
                    }
                }
                else
                {
                    Articles.Clear();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load cached articles: {ex.Message}");
            }
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

        public async Task MarkAllAsReadInActiveStreamAsync()
        {
            var unreadArticles = Articles.Where(a => !a.IsRead).ToList();
            if (unreadArticles.Count == 0) return;

            // 1. Mark all as read locally immediately for smooth UI transition
            foreach (var article in unreadArticles)
            {
                article.IsRead = true;
                UpdateArticleReadStatusInCache(article.Id, true);
                AddPendingRead(article.Id);
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
                    RemovePendingRead(article.Id);
                }
            }).ToList();

            await Task.WhenAll(tasks);
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
                UpdateArticleReadStatusInCache(article.Id, true);
                AddPendingRead(article.Id);
                UpdateLocalUnreadCounts(article.FeedId);
                _notificationService.DismissNotification(article.Id);
            }

            // 2. Call API for each in parallel
            var tasks = articlesToMark.Select(async article =>
            {
                bool success = await _freshRssService.MarkAsReadAsync(article.Id);
                if (success)
                {
                    RemovePendingRead(article.Id);
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
                        var uri = new Uri(article.Link);
                        await Windows.System.Launcher.LaunchUriAsync(uri);
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
                    UpdateArticleReadStatusInCache(article.Id, true);
                    AddPendingRead(article.Id);
                    UpdateLocalUnreadCounts(article.FeedId);
                    _notificationService.DismissNotification(article.Id);
                }

                var tasks = articlesToMark.Select(async article =>
                {
                    bool success = await _freshRssService.MarkAsReadAsync(article.Id);
                    if (success)
                    {
                        RemovePendingRead(article.Id);
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }

            // 3. Turn off multi-select mode
            IsMultiSelectMode = false;
        }

        private List<string> LoadPendingReads()
        {
            try
            {
                if (File.Exists(_pendingReadsFilePath))
                {
                    var json = File.ReadAllText(_pendingReadsFilePath);
                    return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load pending reads: {ex.Message}");
            }
            return new List<string>();
        }

        private HashSet<string> LoadSentNotifications()
        {
            try
            {
                if (File.Exists(_sentNotificationsFilePath))
                {
                    var json = File.ReadAllText(_sentNotificationsFilePath);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        return new HashSet<string>(list);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load sent notifications: {ex.Message}");
            }
            return new HashSet<string>();
        }

        private void SaveSentNotifications()
        {
            try
            {
                // Limit the saved list to the most recent 500 notifications to prevent infinite file growth
                var list = _sentNotificationIds.Take(500).ToList();
                var json = JsonSerializer.Serialize(list);
                File.WriteAllText(_sentNotificationsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save sent notifications: {ex.Message}");
            }
        }

        private void AddPendingRead(string articleId)
        {
            try
            {
                var pending = LoadPendingReads();
                if (!pending.Contains(articleId))
                {
                    pending.Add(articleId);
                    var json = JsonSerializer.Serialize(pending);
                    File.WriteAllText(_pendingReadsFilePath, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to add pending read: {ex.Message}");
            }
        }

        private void RemovePendingRead(string articleId)
        {
            try
            {
                var pending = LoadPendingReads();
                if (pending.Remove(articleId))
                {
                    var json = JsonSerializer.Serialize(pending);
                    File.WriteAllText(_pendingReadsFilePath, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to remove pending read: {ex.Message}");
            }
        }

        private void UpdateArticleReadStatusInCache(string articleId, bool isRead)
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var cache = JsonSerializer.Deserialize<OfflineCache>(json, options);
                    
                    if (cache?.ArticlesByStream != null)
                    {
                        bool modified = false;
                        foreach (var key in cache.ArticlesByStream.Keys)
                        {
                            var articles = cache.ArticlesByStream[key];
                            var article = articles.FirstOrDefault(a => a.Id == articleId);
                            if (article != null)
                            {
                                article.IsRead = isRead;
                                modified = true;
                            }
                        }

                        if (modified)
                        {
                            var serializedJson = JsonSerializer.Serialize(cache);
                            File.WriteAllText(_cacheFilePath, serializedJson);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update article read status in cache: {ex.Message}");
            }
        }

        private async Task SyncPendingReadsAsync(CancellationToken cancellationToken = default)
        {
            var pending = LoadPendingReads();
            if (pending.Count == 0) return;

            EnqueueOnDispatcher(() =>
            {
                SyncStatusText = LocalizationManager.Current.SyncPendingReadsStatus;
            });

            // Run pending mark-as-read requests in parallel
            var tasks = pending.Select(async articleId =>
            {
                try
                {
                    bool success = await _freshRssService.MarkAsReadAsync(articleId, cancellationToken);
                    return (ArticleId: articleId, Success: success);
                }
                catch
                {
                    return (ArticleId: articleId, Success: false);
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
                try
                {
                    var json = JsonSerializer.Serialize(pending);
                    File.WriteAllText(_pendingReadsFilePath, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save pending reads: {ex.Message}");
                }
            }
        }

        private class OfflineCache
        {
            public List<RssCategory> Categories { get; set; } = new();
            public List<RssFeed> Feeds { get; set; } = new();
            public Dictionary<string, List<RssArticle>> ArticlesByStream { get; set; } = new();
        }

        private class AppSettings
        {
            public string ServerUrl { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string ApiPassword { get; set; } = string.Empty;
            public int SyncInterval { get; set; } = 15;
            public bool EnableOpenGraph { get; set; }
            public bool ShowUnreadOnly { get; set; } = false;
            public string ArticleFilter { get; set; } = "All";
            public int MaxReadArticles { get; set; } = 50;
            public string Language { get; set; } = "it";
            public bool UseGridLayout { get; set; }
            public bool OpenLinksInBrowser { get; set; }
            public bool AutoStartWithWindows { get; set; }
            public bool StartMinimizedInTray { get; set; }
        }
    }
}
