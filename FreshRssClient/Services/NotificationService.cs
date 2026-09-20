using System;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FreshRssClient.Helpers;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace FreshRssClient.Services
{
    public interface INotificationService
    {
        void SendArticleNotification(string articleId, string feedTitle, string articleTitle, string? imageUrl);
        void DismissNotification(string articleId);
        void UpdateBadge(int count);
    }

    public class NotificationService : INotificationService
    {
        private const string AppUserModelId = "Maicol.FreshRssClient.App";
        private const string NotificationGroupName = "FreshRssNotifications";

        private static readonly HttpClient ImageDownloadClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(4)
        };

        static NotificationService()
        {
            ImageDownloadClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 FreshRssClient/1.0");
        }

        public void SendArticleNotification(string articleId, string feedTitle, string articleTitle, string? imageUrl)
        {
            if (!string.IsNullOrEmpty(imageUrl) &&
                Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                SafeFireAndForget.Run(async () =>
                {
                    var effectiveImageUrl = await EnsureLocalImageAsync(imageUrl).ConfigureAwait(false);
                    ShowToast(articleId, feedTitle, articleTitle, effectiveImageUrl);
                });
            }
            else
            {
                ShowToast(articleId, feedTitle, articleTitle, imageUrl);
            }
        }

        public static string BuildToastXml(string articleId, string feedTitle, string articleTitle, string? imageUrl)
        {
            var titleEscaped = SecurityElement.Escape(string.Format(LocalizationManager.Current.NewArticleNotificationTitle, feedTitle));
            var bodyEscaped = SecurityElement.Escape(articleTitle);

            string imageNode = string.Empty;
            if (!string.IsNullOrEmpty(imageUrl) && Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
            {
                var imageEscaped = SecurityElement.Escape(imageUrl);
                imageNode = $"<image placement='hero' src='{imageEscaped}'/>";
            }

            return $@"
                <toast launch='articleId={SecurityElement.Escape(articleId)}'>
                    <visual>
                        <binding template='ToastGeneric'>
                            {imageNode}
                            <text>{titleEscaped}</text>
                            <text>{bodyEscaped}</text>
                        </binding>
                    </visual>
                </toast>";
        }

        private void ShowToast(string articleId, string feedTitle, string articleTitle, string? imageUrl)
        {
            try
            {
                var toastXmlString = BuildToastXml(articleId, feedTitle, articleTitle, imageUrl);

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(toastXmlString);
                var toast = new ToastNotification(xmlDoc)
                {
                    Tag = articleId,
                    Group = NotificationGroupName
                };

                var effectiveAumid = GetEffectiveAppUserModelId();

                // Use the standard notifier
                // In a packaged WinUI 3 application, this works out-of-the-box.
                // In unpackaged, we wrap it in a try-catch to prevent a crash.
                try
                {
                    ToastNotificationManager.CreateToastNotifier().Show(toast);
                }
                catch
                {
                    // Fallback using AppUserModelId for unpackaged mode if possible
                    ToastNotificationManager.CreateToastNotifier(effectiveAumid).Show(toast);
                }
            }
            catch (Exception)
            {
                // Silence notification errors to ensure background sync is never interrupted
            }
        }

        private static async Task<string?> EnsureLocalImageAsync(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (uri.IsFile)
            {
                return uri.AbsoluteUri;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return uri.AbsoluteUri;
            }

            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreshRssClient", "NotificationImages");
                Directory.CreateDirectory(folder);
                TryCleanOldCache(folder);

                using var sha = SHA256.Create();
                var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(imageUrl));
                var hashStr = Convert.ToHexString(hashBytes).ToLowerInvariant();

                var ext = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                {
                    ext = ".jpg";
                }

                var localFilePath = Path.Combine(folder, $"{hashStr}{ext}");
                if (File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 0)
                {
                    return new Uri(localFilePath).AbsoluteUri;
                }

                var response = await ImageDownloadClient.GetAsync(uri).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes.Length > 0 && bytes.Length <= 3 * 1024 * 1024)
                    {
                        await File.WriteAllBytesAsync(localFilePath, bytes).ConfigureAwait(false);
                        return new Uri(localFilePath).AbsoluteUri;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] Image download failed: {ex.Message}");
            }

            // Fallback to original URL if local download failed
            return imageUrl;
        }

        private static void TryCleanOldCache(string cacheDir)
        {
            try
            {
                var dir = new DirectoryInfo(cacheDir);
                if (!dir.Exists) return;

                var files = dir.GetFiles();
                if (files.Length > 50)
                {
                    var threshold = DateTime.UtcNow.AddDays(-7);
                    foreach (var file in files)
                    {
                        if (file.LastWriteTimeUtc < threshold)
                        {
                            try { file.Delete(); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        public void DismissNotification(string articleId)
        {
            var effectiveAumid = GetEffectiveAppUserModelId();
            try
            {
                try
                {
                    ToastNotificationManager.History.Remove(articleId, "FreshRssNotifications");
                }
                catch
                {
                    ToastNotificationManager.History.Remove(articleId, "FreshRssNotifications", effectiveAumid);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to dismiss notification: {ex.Message}");
            }
        }

        private static bool IsPackaged()
        {
            try
            {
                return Windows.ApplicationModel.Package.Current != null;
            }
            catch
            {
                return false;
            }
        }

        private static void LogBadgeStatus(string message)
        {
            try
            {
                var localFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreshRssClient");
                System.IO.Directory.CreateDirectory(localFolder);
                var logPath = System.IO.Path.Combine(localFolder, "badge_log.txt");
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                System.IO.File.AppendAllText(logPath, $"[{timestamp}] {message}\n");
                System.Diagnostics.Debug.WriteLine($"[BadgeLog] {message}");
            }
            catch { }
        }

        private string GetEffectiveAppUserModelId()
        {
            try
            {
                if (IsPackaged())
                {
                    return $"{Windows.ApplicationModel.Package.Current.Id.FamilyName}!App";
                }
            }
            catch { }
            return AppUserModelId;
        }

        public void UpdateBadge(int count)
        {
            var effectiveAumid = GetEffectiveAppUserModelId();
            LogBadgeStatus($"UpdateBadge called with count={count}. IsPackaged={IsPackaged()}. EffectiveAumid={effectiveAumid}");
            try
            {
                // Rely exclusively on UWP BadgeUpdateManager for the taskbar badge
                if (count <= 0)
                {
                    try
                    {
                        LogBadgeStatus($"Clearing badge for {effectiveAumid}...");
                        BadgeUpdateManager.CreateBadgeUpdaterForApplication(effectiveAumid).Clear();
                        LogBadgeStatus("Clear successful.");
                    }
                    catch (Exception ex)
                    {
                        LogBadgeStatus($"Clear failed first attempt: {ex.Message}. Trying fallback...");
                        try
                        {
                            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
                            LogBadgeStatus("Clear (Fallback) successful.");
                        }
                        catch (Exception ex2)
                        {
                            LogBadgeStatus($"Clear (Fallback) failed: {ex2.Message}");
                        }
                    }
                }
                else
                {
                    // Get the template for a numeric badge
                    var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);

                    // Set the value in the XML robustly using the root DocumentElement
                    var badgeElement = badgeXml.DocumentElement;
                    if (badgeElement != null)
                    {
                        badgeElement.SetAttribute("value", count.ToString());
                    }
                    else
                    {
                        LogBadgeStatus("Error: DocumentElement is null!");
                    }

                    // Create and send the badge update
                    var badge = new BadgeNotification(badgeXml);
                    try
                    {
                        LogBadgeStatus($"Updating badge to {count} for {effectiveAumid}...");
                        BadgeUpdateManager.CreateBadgeUpdaterForApplication(effectiveAumid).Update(badge);
                        LogBadgeStatus("Update successful.");
                    }
                    catch (Exception ex)
                    {
                        LogBadgeStatus($"Update failed first attempt: {ex.Message}. Trying fallback...");
                        try
                        {
                            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badge);
                            LogBadgeStatus("Update (Fallback) successful.");
                        }
                        catch (Exception ex2)
                        {
                            LogBadgeStatus($"Update (Fallback) failed: {ex2.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogBadgeStatus($"Outer UpdateBadge caught critical exception: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
