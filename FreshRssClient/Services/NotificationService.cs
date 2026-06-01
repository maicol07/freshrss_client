using System;
using System.Security;
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

        public void SendArticleNotification(string articleId, string feedTitle, string articleTitle, string? imageUrl)
        {
            try
            {
                // Escape text for safety in XML
                var titleEscaped = SecurityElement.Escape(string.Format(LocalizationManager.Current.NewArticleNotificationTitle, feedTitle));
                var bodyEscaped = SecurityElement.Escape(articleTitle);

                string imageNode = string.Empty;
                if (!string.IsNullOrEmpty(imageUrl) && Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
                {
                    // Escape image URL
                    var imageEscaped = SecurityElement.Escape(imageUrl);
                    imageNode = $"<image placement='thumbnail' src='{imageEscaped}'/>";
                }

                var toastXmlString = $@"
                <toast launch='articleId={SecurityElement.Escape(articleId)}'>
                    <visual>
                        <binding template='ToastGeneric'>
                            <text>{titleEscaped}</text>
                            <text>{bodyEscaped}</text>
                            {imageNode}
                        </binding>
                    </visual>
                </toast>";

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(toastXmlString);
                var toast = new ToastNotification(xmlDoc)
                {
                    Tag = articleId,
                    Group = "FreshRssNotifications"
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
                var logPath = @"C:\Users\Maicol\AntigravityProjects\freshrss-client\badge_log.txt";
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
