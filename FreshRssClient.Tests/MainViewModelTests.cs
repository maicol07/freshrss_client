using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;
using FreshRssClient.Services;
using FreshRssClient.ViewModels;

namespace FreshRssClient.Tests
{
    [NotInParallel]
    public class MainViewModelTests
    {
        private string _tempDataFolder = string.Empty;

        [Before(Test)]
        public void SetUp()
        {
            _tempDataFolder = Path.Combine(Directory.GetCurrentDirectory(), "TestData_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDataFolder);
        }

        [After(Test)]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDataFolder))
                {
                    Directory.Delete(_tempDataFolder, true);
                }
            }
            catch
            {
                // Silence cleanup errors
            }
        }

        [Test]
        public async Task TestMainViewModel_InitialAppSync_Success()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();

            var feed1 = new RssFeed { Id = "feed1", Title = "Tech", UnreadCount = 3 };
            var cat1 = new RssCategory { Id = "cat1", Title = "Technology", UnreadCount = 3 };
            cat1.Feeds.Add(feed1);

            var article1 = new RssArticle
            {
                Id = "art1",
                Title = "New Tech Release",
                PublishDate = DateTime.Now,
                FeedId = "feed1",
                FeedTitle = "Tech",
                IsRead = false
            };

            fakeService.CategoriesToReturn.Add(cat1);
            fakeService.FeedsToReturn.Add(feed1);
            fakeService.ArticlesToReturn.Add(article1);

            // Act
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);

            // Wait briefly for the asynchronous initialization task to run
            await Task.Delay(200);

            // Assert
            await Assert.That(fakeService.AuthenticateCallCount).IsGreaterThanOrEqualTo(1);
            await Assert.That(fakeService.FetchSubsCallCount).IsGreaterThanOrEqualTo(1);
            await Assert.That(fakeService.FetchArticlesCallCount).IsGreaterThanOrEqualTo(1);
            
            await Assert.That(viewModel.ConnectionStatusText).IsEqualTo("Connesso");
            await Assert.That(viewModel.Categories).Count().IsEqualTo(1);
            await Assert.That(viewModel.Articles).Count().IsEqualTo(1);
            await Assert.That(viewModel.UnreadCount).IsEqualTo(3);
            await Assert.That(fakeNotification.LastBadgeCount).IsEqualTo(3);
        }

        [Test]
        public async Task TestMainViewModel_InitialAppSync_OfflineMode()
        {
            // Arrange
            var fakeService = new FakeFreshRssService
            {
                AuthenticateResult = false,
                LastConnectionFailed = true
            };
            var fakeNotification = new FakeNotificationService();

            // Pre-seed offline cache file so we verify that the view model loads it under offline mode
            var cacheFile = Path.Combine(_tempDataFolder, "cache.json");
            var offlineCache = new OfflineCacheDto
            {
                Categories = new List<RssCategoryDto>
                {
                    new RssCategoryDto
                    {
                        Id = "cat1_cached",
                        Title = "Cached Tech",
                        UnreadCount = 4,
                        Feeds = new List<RssFeedDto>
                        {
                            new RssFeedDto { Id = "feed1_cached", Title = "Cached Feed", UnreadCount = 4 }
                        }
                    }
                },
                Feeds = new List<RssFeedDto>
                {
                    new RssFeedDto { Id = "feed1_cached", Title = "Cached Feed", UnreadCount = 4 }
                },
                ArticlesByStream = new Dictionary<string, List<RssArticleDto>>
                {
                    { "all", new List<RssArticleDto>
                        {
                            new RssArticleDto { Id = "art_cached", Title = "Cached Article", FeedId = "feed1_cached", IsRead = false }
                        }
                    }
                }
            };
            File.WriteAllText(cacheFile, JsonSerializer.Serialize(offlineCache));

            // Act
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);
            await Task.Delay(200);

            // Assert
            await Assert.That(fakeService.AuthenticateCallCount).IsGreaterThanOrEqualTo(1);
            await Assert.That(viewModel.ConnectionStatusText).Contains("Modalità offline");
            
            // Check that pre-seeded cache got successfully loaded
            await Assert.That(viewModel.Categories).Count().IsEqualTo(1);
            await Assert.That(viewModel.Categories[0].Id).IsEqualTo("cat1_cached");
            await Assert.That(viewModel.UnreadCount).IsEqualTo(4);
            await Assert.That(viewModel.Articles).Count().IsEqualTo(1);
            await Assert.That(viewModel.Articles[0].Id).IsEqualTo("art_cached");
        }

        [Test]
        public async Task TestMainViewModel_SaveSettings_SavesToFileAndReinitializes()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);

            // Act
            viewModel.ServerUrl = "https://freshrss.example.org";
            viewModel.Username = "tester";
            viewModel.ApiPassword = "secure_password";
            viewModel.SyncInterval = 30;
            viewModel.Language = "en";

            viewModel.SaveSettingsCommand.Execute(null);
            await Task.Delay(100);

            // Assert
            var settingsFile = Path.Combine(_tempDataFolder, "settings.json");
            await Assert.That(File.Exists(settingsFile)).IsTrue();

            var settingsContent = File.ReadAllText(settingsFile);
            await Assert.That(settingsContent).Contains("\"ServerUrl\":\"https://freshrss.example.org\"");
            await Assert.That(settingsContent).Contains("\"Username\":\"tester\"");
            await Assert.That(settingsContent).Contains("\"SyncInterval\":30");
            await Assert.That(settingsContent).Contains("\"Language\":\"en\"");
        }

        [Test]
        public async Task TestMainViewModel_MarkArticleAsRead_UpdatesLocalStateAndQueuesPending()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();
            
            // Construct VM
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);
            
            // Populate category, feed, and articles collections manually to isolate the MarkAsRead logic
            var feed = new RssFeed { Id = "feed_xyz", Title = "Feed XYZ", UnreadCount = 5 };
            var category = new RssCategory { Id = "cat_xyz", Title = "Category XYZ", UnreadCount = 5 };
            category.Feeds.Add(feed);
            
            viewModel.Categories.Add(category);
            viewModel.UnreadCount = 5;
            
            var article = new RssArticle
            {
                Id = "article_1",
                Title = "Article 1",
                FeedId = "feed_xyz",
                IsRead = false
            };
            viewModel.Articles.Add(article);

            // Act
            viewModel.SelectedArticle = article;
            await Task.Delay(100);

            // Assert
            // 1. Article read status is updated
            await Assert.That(article.IsRead).IsTrue();
            
            // 2. Unread counts decremented locally
            await Assert.That(feed.UnreadCount).IsEqualTo(4);
            await Assert.That(category.UnreadCount).IsEqualTo(4);
            await Assert.That(viewModel.UnreadCount).IsEqualTo(4);
            await Assert.That(fakeNotification.LastBadgeCount).IsEqualTo(4);

            // 3. MarkAsReadAsync was successfully called on service with the correct article ID
            await Assert.That(fakeService.MarkedReadArticleIds).Contains("article_1");
        }

        [Test]
        public async Task TestMainViewModel_MarkArticleAsRead_Offline_QueuesPendingRead()
        {
            // Arrange
            var fakeService = new FakeFreshRssService
            {
                MarkAsReadResult = false // MarkAsRead fails (e.g. offline)
            };
            var fakeNotification = new FakeNotificationService();
            
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);
            
            var feed = new RssFeed { Id = "feed_xyz", Title = "Feed XYZ", UnreadCount = 2 };
            var category = new RssCategory { Id = "cat_xyz", Title = "Category XYZ", UnreadCount = 2 };
            category.Feeds.Add(feed);
            viewModel.Categories.Add(category);
            viewModel.UnreadCount = 2;
            
            var article = new RssArticle
            {
                Id = "article_offline",
                Title = "Offline Read",
                FeedId = "feed_xyz",
                IsRead = false
            };
            viewModel.Articles.Add(article);

            // Act
            viewModel.SelectedArticle = article;
            await Task.Delay(100);

            // Assert
            // Local state should still be updated instantly for smooth UI
            await Assert.That(article.IsRead).IsTrue();
            await Assert.That(viewModel.UnreadCount).IsEqualTo(1);
            
            // Article should be queued to pending reads file
            var pendingFile = Path.Combine(_tempDataFolder, "pending_reads.json");
            await Assert.That(File.Exists(pendingFile)).IsTrue();

            var pendingContent = File.ReadAllText(pendingFile);
            await Assert.That(pendingContent).Contains("article_offline");
        }

        [Test]
        public async Task TestMainViewModel_SyncPendingReads_SendsPendingToApi()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();

            // Populate mock categories/feeds to prevent null issues
            var feed1 = new RssFeed { Id = "feed1", Title = "Feed 1", UnreadCount = 1 };
            fakeService.FeedsToReturn.Add(feed1);

            // Pre-seed pending_reads.json
            var pendingFile = Path.Combine(_tempDataFolder, "pending_reads.json");
            var pendingList = new List<string> { "pending_article_1", "pending_article_2" };
            File.WriteAllText(pendingFile, JsonSerializer.Serialize(pendingList));

            // Act
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);
            
            // Wait for initial login & pending sync to run
            await Task.Delay(200);

            // Assert
            // Verify service got called to mark both articles as read on server
            await Assert.That(fakeService.MarkedReadArticleIds).Contains("pending_article_1");
            await Assert.That(fakeService.MarkedReadArticleIds).Contains("pending_article_2");

            // Verify pending list file was cleared
            var pendingContent = File.ReadAllText(pendingFile);
            await Assert.That(pendingContent).IsEqualTo("[]");
        }

        [Test]
        public async Task TestMainViewModel_ArticleFilter_FiltersLocallyAndUpdatesShowUnreadOnly()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);

            // Populate some unread and read articles
            var art1 = new RssArticle { Id = "a1", IsRead = false, Title = "Unread Art" };
            var art2 = new RssArticle { Id = "a2", IsRead = true, Title = "Read Art" };
            fakeService.ArticlesToReturn.Add(art1);
            fakeService.ArticlesToReturn.Add(art2);

            // Act & Assert
            viewModel.ArticleFilter = "Read";
            await Task.Delay(100);
            await Assert.That(viewModel.ShowUnreadOnly).IsFalse();
            
            viewModel.ArticleFilter = "Unread";
            await Task.Delay(100);
            await Assert.That(viewModel.ShowUnreadOnly).IsTrue();
        }

        [Test]
        public async Task TestMainViewModel_MarkAllAsReadInActiveStream_UpdatesCountsAndCallsApi()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);

            var feed = new RssFeed { Id = "feed/all", Title = "Feed All", UnreadCount = 3 };
            var category = new RssCategory { Id = "cat_all", Title = "Category All", UnreadCount = 3 };
            category.Feeds.Add(feed);
            viewModel.Categories.Add(category);
            viewModel.UnreadCount = 3;

            var article = new RssArticle { Id = "art_all", FeedId = "feed/all", IsRead = false };
            viewModel.Articles.Add(article);

            viewModel.ActiveStreamId = "feed/all";

            // Act
            await viewModel.MarkAllAsReadInActiveStreamAsync();

            // Assert
            await Assert.That(article.IsRead).IsTrue();
            await Assert.That(feed.UnreadCount).IsEqualTo(2);
            await Assert.That(category.UnreadCount).IsEqualTo(2);
            await Assert.That(viewModel.UnreadCount).IsEqualTo(2);
            await Assert.That(fakeService.MarkAllAsReadCallCount).IsEqualTo(1);
            await Assert.That(fakeService.LastMarkAllAsReadStreamId).IsEqualTo("feed/all");
        }

        [Test]
        public async Task TestMainViewModel_MarkSelectedAsRead_UpdatesSelectedListAndCounts()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);

            var feed = new RssFeed { Id = "feed_sel", Title = "Feed Sel", UnreadCount = 4 };
            var category = new RssCategory { Id = "cat_sel", Title = "Category Sel", UnreadCount = 4 };
            category.Feeds.Add(feed);
            viewModel.Categories.Add(category);
            viewModel.UnreadCount = 4;

            var art1 = new RssArticle { Id = "art1", FeedId = "feed_sel", IsRead = false };
            var art2 = new RssArticle { Id = "art2", FeedId = "feed_sel", IsRead = false };
            viewModel.Articles.Add(art1);
            viewModel.Articles.Add(art2);

            viewModel.IsMultiSelectMode = true;
            viewModel.SetSelectedArticles(new List<RssArticle> { art1, art2 });

            // Act
            await viewModel.MarkSelectedAsReadAsync();

            // Assert
            await Assert.That(art1.IsRead).IsTrue();
            await Assert.That(art2.IsRead).IsTrue();
            await Assert.That(feed.UnreadCount).IsEqualTo(2);
            await Assert.That(viewModel.UnreadCount).IsEqualTo(2);
            await Assert.That(fakeService.MarkedReadArticleIds).Contains("art1");
            await Assert.That(fakeService.MarkedReadArticleIds).Contains("art2");
            await Assert.That(viewModel.IsMultiSelectMode).IsFalse();
        }

        [Test]
        public async Task TestMainViewModel_MarkArticleAsRead_DismissesNotification()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);

            var feed = new RssFeed { Id = "feed_dismiss", Title = "Feed Dismiss", UnreadCount = 1 };
            var category = new RssCategory { Id = "cat_dismiss", Title = "Category Dismiss", UnreadCount = 1 };
            category.Feeds.Add(feed);
            viewModel.Categories.Add(category);
            viewModel.UnreadCount = 1;

            var article = new RssArticle { Id = "art_dismiss", FeedId = "feed_dismiss", IsRead = false };
            viewModel.Articles.Add(article);

            // Act
            viewModel.SelectedArticle = article;
            await Task.Delay(100);

            // Assert
            await Assert.That(fakeNotification.DismissedArticleIds).Contains("art_dismiss");
        }

        [Test]
        public async Task TestMainViewModel_SyncDeduplicatesNotifications()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();

            var feed = new RssFeed { Id = "feed1", Title = "Tech", UnreadCount = 1 };
            var cat = new RssCategory { Id = "cat1", Title = "Technology", UnreadCount = 1 };
            cat.Feeds.Add(feed);
            fakeService.CategoriesToReturn.Add(cat);
            fakeService.FeedsToReturn.Add(feed);

            var existingArticle = new RssArticle
            {
                Id = "existing_art",
                Title = "Old Article",
                FeedId = "feed1",
                IsRead = false
            };
            fakeService.ArticlesToReturn.Add(existingArticle);

            // Act - Part 1: First load
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);
            await Task.Delay(200);

            // Assert - First load should populate sent_notifications.json without sending a toast notification for existing unread articles
            await Assert.That(fakeNotification.NotificationsSent).IsEmpty();

            // Act - Part 2: Add a new unread article and sync
            var newArticle = new RssArticle
            {
                Id = "new_art",
                Title = "New Article",
                FeedId = "feed1",
                IsRead = false
            };
            fakeService.ArticlesToReturn.Clear();
            fakeService.ArticlesToReturn.Add(existingArticle);
            fakeService.ArticlesToReturn.Add(newArticle);

            await viewModel.SyncCommand.ExecuteAsync(null);
            await Task.Delay(100);

            // Assert - Toast notifications sent should only contain the new article, not the existing one
            await Assert.That(fakeNotification.NotificationsSent).Count().IsEqualTo(1);
            await Assert.That(fakeNotification.NotificationsSent[0].ArticleId).IsEqualTo("new_art");

            // Act - Part 3: Sync again with no new articles
            await viewModel.SyncCommand.ExecuteAsync(null);
            await Task.Delay(100);

            // Assert - Still only 1 notification sent total
            await Assert.That(fakeNotification.NotificationsSent).Count().IsEqualTo(1);
        }

        [Test]
        public async Task TestMainViewModel_ArticleFilter_PersistsAndLoadsCorrectly()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();
            
            // 1. Initial run: Filter should default to "All"
            using (var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder))
            {
                await Task.Delay(100);
                await Assert.That(viewModel.ArticleFilter).IsEqualTo("All");
                await Assert.That(viewModel.ShowUnreadOnly).IsFalse();

                // Change filter to "Unread" which should auto-save settings
                viewModel.ArticleFilter = "Unread";
                await Task.Delay(100);
            }

            // 2. Second run: Should load "Unread" from settings
            using (var viewModel2 = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder))
            {
                await Task.Delay(100);
                await Assert.That(viewModel2.ArticleFilter).IsEqualTo("Unread");
                await Assert.That(viewModel2.ShowUnreadOnly).IsTrue();
            }
        }

        [Test]
        public async Task TestMainViewModel_SearchQuery_FiltersLocally()
        {
            // Arrange
            var fakeService = new FakeFreshRssService();
            var fakeNotification = new FakeNotificationService();
            using var viewModel = new MainViewModel(fakeService, fakeNotification, null, _tempDataFolder);

            var art1 = new RssArticle { Id = "a1", Title = "Microsoft Windows Update", IsRead = false };
            var art2 = new RssArticle { Id = "a2", Title = "Google Gemini Announcement", IsRead = false };
            var art3 = new RssArticle { Id = "a3", Title = "FreshRSS client with WinUI 3", IsRead = false };

            fakeService.ArticlesToReturn.Add(art1);
            fakeService.ArticlesToReturn.Add(art2);
            fakeService.ArticlesToReturn.Add(art3);

            // Fetch them to populate _currentAllArticles
            await viewModel.SyncCommand.ExecuteAsync(null);
            await Task.Delay(100);

            // Act - Local search
            viewModel.SearchQuery = "Gemini";

            // Assert
            await Assert.That(viewModel.Articles).Count().IsEqualTo(1);
            await Assert.That(viewModel.Articles[0].Id).IsEqualTo("a2");

            // Act - Reset search
            viewModel.SearchQuery = "";

            // Assert
            await Assert.That(viewModel.Articles).Count().IsEqualTo(3);
        }
    }

    // Fake service implementation for MainViewModel testing
    public class FakeFreshRssService : IFreshRssService
    {
        public bool LastConnectionFailed { get; set; } = false;
        public bool AuthenticateResult { get; set; } = true;
        public List<RssArticle> ArticlesToReturn { get; set; } = new();
        public bool MarkAsReadResult { get; set; } = true;
        public List<RssCategory> CategoriesToReturn { get; set; } = new();
        public List<RssFeed> FeedsToReturn { get; set; } = new();

        public int AuthenticateCallCount { get; private set; }
        public int FetchArticlesCallCount { get; private set; }
        public int MarkAsReadCallCount { get; private set; }
        public int FetchSubsCallCount { get; private set; }
        public int MarkAllAsReadCallCount { get; private set; }

        public List<string> MarkedReadArticleIds { get; } = new();
        public string? LastMarkAllAsReadStreamId { get; private set; }
        public bool MarkAllAsReadResult { get; set; } = true;

        public Task<bool> AuthenticateAsync(string serverUrl, string username, string apiPassword, CancellationToken cancellationToken = default)
        {
            AuthenticateCallCount++;
            return Task.FromResult(AuthenticateResult);
        }

        public Task<List<RssArticle>> FetchArticlesAsync(string? streamId, bool showUnreadOnly, int maxReadArticles, bool enableOpenGraphScrape, string? searchQuery = null, CancellationToken cancellationToken = default)
        {
            FetchArticlesCallCount++;
            return Task.FromResult(ArticlesToReturn);
        }

        public Task<bool> MarkAsReadAsync(string articleId, CancellationToken cancellationToken = default)
        {
            MarkAsReadCallCount++;
            MarkedReadArticleIds.Add(articleId);
            return Task.FromResult(MarkAsReadResult);
        }

        public Task<bool> MarkAllAsReadAsync(string? streamId, CancellationToken cancellationToken = default)
        {
            MarkAllAsReadCallCount++;
            LastMarkAllAsReadStreamId = streamId;
            return Task.FromResult(MarkAllAsReadResult);
        }

        public Task<(List<RssCategory> Categories, List<RssFeed> Feeds)> FetchSubscriptionsAndUnreadCountsAsync(CancellationToken cancellationToken = default)
        {
            FetchSubsCallCount++;
            return Task.FromResult((CategoriesToReturn, FeedsToReturn));
        }
    }

    // Fake notification service implementation for MainViewModel testing
    public class FakeNotificationService : INotificationService
    {
        public List<(string ArticleId, string FeedTitle, string ArticleTitle, string? ImageUrl)> NotificationsSent { get; } = new();
        public List<string> DismissedArticleIds { get; } = new();
        public int LastBadgeCount { get; private set; } = -1;

        public void SendArticleNotification(string articleId, string feedTitle, string articleTitle, string? imageUrl)
        {
            NotificationsSent.Add((articleId, feedTitle, articleTitle, imageUrl));
        }

        public void DismissNotification(string articleId)
        {
            DismissedArticleIds.Add(articleId);
        }

        public void UpdateBadge(int count)
        {
            LastBadgeCount = count;
        }
    }

    // Data Transfer Objects matching exact serialization keys for Cache verification
    internal class OfflineCacheDto
    {
        public List<RssCategoryDto> Categories { get; set; } = new();
        public List<RssFeedDto> Feeds { get; set; } = new();
        public Dictionary<string, List<RssArticleDto>> ArticlesByStream { get; set; } = new();
    }

    internal class RssCategoryDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int UnreadCount { get; set; }
        public List<RssFeedDto> Feeds { get; set; } = new();
    }

    internal class RssFeedDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public int UnreadCount { get; set; }
        public string IconUrl { get; set; } = string.Empty;
    }

    internal class RssArticleDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public DateTime PublishDate { get; set; } = DateTime.Now;
        public string Summary { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string FeedTitle { get; set; } = string.Empty;
        public string FeedId { get; set; } = string.Empty;
        public string FeedIconUrl { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public string? ImageUrl { get; set; }
    }
}
