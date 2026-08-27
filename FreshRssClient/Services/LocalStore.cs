using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FreshRssClient.Models;
using Windows.Security.Credentials;

namespace FreshRssClient.Services
{
    public interface ILocalStore
    {
        /// <summary>
        /// True when credentials are stored in the Windows credential locker.
        /// Disabled when a custom data folder is used (tests).
        /// </summary>
        bool CredentialLockerEnabled { get; }

        /// <summary>
        /// Loads persisted settings together with the plaintext password of legacy
        /// settings files, which is empty once the credential locker owns it.
        /// </summary>
        (AppSettings? Settings, string LegacyPassword) LoadSettings();

        void SaveSettings(AppSettings settings, string apiPassword);

        OfflineCache? LoadCache();

        void SaveCache(List<RssCategory> categories, List<RssFeed> feeds, List<RssArticle> articles, string activeKey);

        void UpdateArticleReadStatusInCache(string articleId, bool isRead);

        void UpdateArticleImagesInCache(IReadOnlyDictionary<string, string> imagesByArticleId);

        Dictionary<string, bool> LoadPendingReads();

        void SetPendingReadState(string articleId, bool isRead);

        void RemovePendingReadState(string articleId);

        void SavePendingReads(Dictionary<string, bool> pendingReads);

        HashSet<string> LoadSentNotifications();

        void SaveSentNotifications(IEnumerable<string> notificationIds);

        string? LoadCredential(string username);
    }

    /// <summary>
    /// Owns every on-disk artifact of the app: settings, offline cache, pending read
    /// states, sent notification ids and the credential locker entry.
    /// </summary>
    public class LocalStore : ILocalStore
    {
        private const string CredentialResource = "FreshRssClient";
        private const int MaxSentNotifications = 500;

        private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

        private readonly object _fileLock = new();
        private readonly string _settingsFilePath;
        private readonly string _cacheFilePath;
        private readonly string _pendingReadsFilePath;
        private readonly string _sentNotificationsFilePath;

        public bool CredentialLockerEnabled { get; }

        public LocalStore(string? customDataFolder = null)
        {
            var localFolder = customDataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreshRssClient");
            CredentialLockerEnabled = customDataFolder == null;
            Directory.CreateDirectory(localFolder);
            _settingsFilePath = Path.Combine(localFolder, "settings.json");
            _cacheFilePath = Path.Combine(localFolder, "cache.json");
            _pendingReadsFilePath = Path.Combine(localFolder, "pending_reads.json");
            _sentNotificationsFilePath = Path.Combine(localFolder, "sent_notifications.json");
        }

        public (AppSettings? Settings, string LegacyPassword) LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    return (null, string.Empty);
                }

                var json = File.ReadAllText(_settingsFilePath);
                string legacyPassword = string.Empty;
                using (var document = JsonDocument.Parse(json))
                {
                    if (document.RootElement.TryGetProperty("ApiPassword", out var passwordElement))
                    {
                        legacyPassword = passwordElement.GetString() ?? string.Empty;
                    }
                }

                return (JsonSerializer.Deserialize<AppSettings>(json), legacyPassword);
            }
            catch
            {
                // Fallback to defaults
                return (null, string.Empty);
            }
        }

        public void SaveSettings(AppSettings settings, string apiPassword)
        {
            try
            {
                lock (_fileLock)
                {
                    WriteJsonAtomically(_settingsFilePath, JsonSerializer.Serialize(settings));
                }
                SaveCredential(settings.Username, apiPassword);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public OfflineCache? LoadCache()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    return JsonSerializer.Deserialize<OfflineCache>(File.ReadAllText(_cacheFilePath), CaseInsensitive);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load cache: {ex.Message}");
            }
            return null;
        }

        public void SaveCache(List<RssCategory> categories, List<RssFeed> feeds, List<RssArticle> articles, string activeKey)
        {
            lock (_fileLock)
            {
                try
                {
                    var cache = ReadCacheUnlocked() ?? new OfflineCache();

                    cache.Categories = categories;
                    cache.Feeds = feeds;
                    cache.ArticlesByStream ??= new Dictionary<string, List<RssArticle>>();
                    cache.ArticlesByStream[activeKey] = articles;

                    WriteJsonAtomically(_cacheFilePath, JsonSerializer.Serialize(cache));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save cache: {ex.Message}");
                }
            }
        }

        public void UpdateArticleReadStatusInCache(string articleId, bool isRead)
        {
            lock (_fileLock)
            {
                try
                {
                    var cache = ReadCacheUnlocked();
                    if (cache?.ArticlesByStream == null)
                    {
                        return;
                    }

                    bool modified = false;
                    foreach (var articles in cache.ArticlesByStream.Values)
                    {
                        var article = articles.FirstOrDefault(a => a.Id == articleId);
                        if (article != null)
                        {
                            article.IsRead = isRead;
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        WriteJsonAtomically(_cacheFilePath, JsonSerializer.Serialize(cache));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to update article read status in cache: {ex.Message}");
                }
            }
        }

        public void UpdateArticleImagesInCache(IReadOnlyDictionary<string, string> imagesByArticleId)
        {
            lock (_fileLock)
            {
                try
                {
                    var cache = ReadCacheUnlocked();
                    if (cache?.ArticlesByStream == null)
                    {
                        return;
                    }

                    bool modified = false;
                    foreach (var articles in cache.ArticlesByStream.Values)
                    {
                        foreach (var article in articles)
                        {
                            if (string.IsNullOrEmpty(article.ImageUrl) &&
                                imagesByArticleId.TryGetValue(article.Id, out var imageUrl))
                            {
                                article.ImageUrl = imageUrl;
                                modified = true;
                            }
                        }
                    }

                    if (modified)
                    {
                        WriteJsonAtomically(_cacheFilePath, JsonSerializer.Serialize(cache));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to update article images in cache: {ex.Message}");
                }
            }
        }

        public Dictionary<string, bool> LoadPendingReads()
        {
            lock (_fileLock)
            {
                return LoadPendingReadsUnlocked();
            }
        }

        public void SetPendingReadState(string articleId, bool isRead)
        {
            lock (_fileLock)
            {
                try
                {
                    var pending = LoadPendingReadsUnlocked();
                    pending[articleId] = isRead;
                    WriteJsonAtomically(_pendingReadsFilePath, JsonSerializer.Serialize(pending));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save pending article state: {ex.Message}");
                }
            }
        }

        public void RemovePendingReadState(string articleId)
        {
            lock (_fileLock)
            {
                try
                {
                    var pending = LoadPendingReadsUnlocked();
                    if (pending.Remove(articleId))
                    {
                        WriteJsonAtomically(_pendingReadsFilePath, JsonSerializer.Serialize(pending));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to remove pending article state: {ex.Message}");
                }
            }
        }

        public void SavePendingReads(Dictionary<string, bool> pendingReads)
        {
            lock (_fileLock)
            {
                try
                {
                    WriteJsonAtomically(_pendingReadsFilePath, JsonSerializer.Serialize(pendingReads));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save pending reads: {ex.Message}");
                }
            }
        }

        public HashSet<string> LoadSentNotifications()
        {
            try
            {
                if (File.Exists(_sentNotificationsFilePath))
                {
                    var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_sentNotificationsFilePath));
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

        public void SaveSentNotifications(IEnumerable<string> notificationIds)
        {
            try
            {
                // Limit the saved list to the most recent 500 notifications to prevent infinite file growth
                var list = notificationIds.Take(MaxSentNotifications).ToList();
                File.WriteAllText(_sentNotificationsFilePath, JsonSerializer.Serialize(list));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save sent notifications: {ex.Message}");
            }
        }

        public string? LoadCredential(string username)
        {
            if (!CredentialLockerEnabled || string.IsNullOrWhiteSpace(username)) return null;

            try
            {
                var credential = new PasswordVault().Retrieve(CredentialResource, username);
                credential.RetrievePassword();
                return credential.Password;
            }
            catch
            {
                return null;
            }
        }

        private void SaveCredential(string username, string apiPassword)
        {
            if (!CredentialLockerEnabled || string.IsNullOrWhiteSpace(username)) return;

            try
            {
                var vault = new PasswordVault();
                try
                {
                    foreach (var credential in vault.FindAllByResource(CredentialResource))
                    {
                        vault.Remove(credential);
                    }
                }
                catch
                {
                    // The vault throws when no matching credentials exist.
                }

                if (!string.IsNullOrEmpty(apiPassword))
                {
                    vault.Add(new PasswordCredential(CredentialResource, username, apiPassword));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save credentials: {ex.Message}");
            }
        }

        private OfflineCache? ReadCacheUnlocked()
        {
            if (!File.Exists(_cacheFilePath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<OfflineCache>(File.ReadAllText(_cacheFilePath), CaseInsensitive);
            }
            catch
            {
                return null;
            }
        }

        private Dictionary<string, bool> LoadPendingReadsUnlocked()
        {
            try
            {
                if (File.Exists(_pendingReadsFilePath))
                {
                    var json = File.ReadAllText(_pendingReadsFilePath);
                    try
                    {
                        return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new();
                    }
                    catch (JsonException)
                    {
                        var legacyReads = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                        return legacyReads.ToDictionary(id => id, _ => true);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load pending reads: {ex.Message}");
            }
            return new Dictionary<string, bool>();
        }

        private void WriteJsonAtomically(string path, string json)
        {
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, true);
        }
    }
}
