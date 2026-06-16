using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FreshRssClient.Services
{
    public interface ILocalization
    {
        string AppTitle { get; }
        string FeedsTab { get; }
        string SettingsTab { get; }
        string ServerUrlLabel { get; }
        string UsernameLabel { get; }
        string ApiPasswordLabel { get; }
        string UpdateIntervalLabel { get; }
        string EnableOpenGraphLabel { get; }
        string LanguageLabel { get; }
        string SaveButton { get; }
        string SavedSuccess { get; }
        string SyncNowButton { get; }
        string SyncingStatus { get; }
        string SyncSuccess { get; }
        string SyncError { get; }
        string UnreadArticlesHeader { get; }
        string NoArticles { get; }
        string NoArticlesSubtitle { get; }
        string MarkAsReadButton { get; }
        string OpenInBrowser { get; }
        string IntervalMinutes { get; }
        string StatusConnected { get; }
        string StatusDisconnected { get; }
        string NewArticleNotificationTitle { get; }
        string SettingsSaved { get; }
        string ShowUnreadOnlyLabel { get; }
        string MaxReadArticlesLabel { get; }
        string LimitArticlesCount { get; }
        string UncategorizedGroup { get; }
        string OfflineModeStatus { get; }
        string SyncPendingReadsStatus { get; }
        string OpenLinksInBrowserLabel { get; }
        string UseGridLayoutLabel { get; }
        string AutoStartLabel { get; }
        string StartMinimizedLabel { get; }
        string BackToGridButton { get; }
        string FilterLabel { get; }
        string FilterAll { get; }
        string FilterUnread { get; }
        string FilterRead { get; }
        string MarkAllAsRead { get; }
        string MassMarkAsRead { get; }
        string MassOpen { get; }
        string SelectedArticlesSuffix { get; }
        string SearchPlaceholder { get; }
        string SelectAll { get; }

        // SettingsPage labels
        string AccountExpanderHeader { get; }
        string AccountExpanderDesc { get; }
        string ServerUrlCardDesc { get; }
        string UsernameCardDesc { get; }
        string PasswordCardDesc { get; }
        string SyncExpanderHeader { get; }
        string SyncExpanderDesc { get; }
        string IntervalCardDesc { get; }
        string MaxReadCardDesc { get; }
        string ReadingExpanderHeader { get; }
        string ReadingExpanderDesc { get; }
        string OpenGraphCardDesc { get; }
        string DefaultFilterCardHeader { get; }
        string DefaultFilterCardDesc { get; }
        string OpenInBrowserCardDesc { get; }
        string SystemExpanderHeader { get; }
        string SystemExpanderDesc { get; }
        string AutoStartCardDesc { get; }
        string StartMinimizedCardDesc { get; }
        string LanguageCardDesc { get; }

        // ArticlesPage labels
        string ListViewTooltip { get; }
        string GridViewTooltip { get; }
        string ConfirmTitle { get; }
        string ConfirmMarkReadContent { get; }
        string ConfirmOpenBrowserContent { get; }
        string YesButton { get; }
        string CancelButton { get; }
        string ContextMarkUnread { get; }
        string ContextMarkRead { get; }
        string ContextDeselect { get; }
        string ContextSelect { get; }

        // Tray icon labels
        string UnreadTraySuffix { get; }
        string TrayRestore { get; }
        string TraySync { get; }
        string TrayExit { get; }
    }

    public class ResourceManagerStrings : ILocalization
    {
        private readonly System.Resources.ResourceManager _resourceManager;
        private readonly System.Globalization.CultureInfo _culture;

        public ResourceManagerStrings(System.Globalization.CultureInfo culture)
        {
            _resourceManager = new System.Resources.ResourceManager("FreshRssClient.Resources.Strings", typeof(ResourceManagerStrings).Assembly);
            _culture = culture;
        }

        private string Get(string key) => _resourceManager.GetString(key, _culture) ?? key;

        public string AppTitle => Get(nameof(AppTitle));
        public string FeedsTab => Get(nameof(FeedsTab));
        public string SettingsTab => Get(nameof(SettingsTab));
        public string ServerUrlLabel => Get(nameof(ServerUrlLabel));
        public string UsernameLabel => Get(nameof(UsernameLabel));
        public string ApiPasswordLabel => Get(nameof(ApiPasswordLabel));
        public string UpdateIntervalLabel => Get(nameof(UpdateIntervalLabel));
        public string EnableOpenGraphLabel => Get(nameof(EnableOpenGraphLabel));
        public string LanguageLabel => Get(nameof(LanguageLabel));
        public string SaveButton => Get(nameof(SaveButton));
        public string SavedSuccess => Get(nameof(SavedSuccess));
        public string SyncNowButton => Get(nameof(SyncNowButton));
        public string SyncingStatus => Get(nameof(SyncingStatus));
        public string SyncSuccess => Get(nameof(SyncSuccess));
        public string SyncError => Get(nameof(SyncError));
        public string UnreadArticlesHeader => Get(nameof(UnreadArticlesHeader));
        public string NoArticles => Get(nameof(NoArticles));
        public string NoArticlesSubtitle => Get(nameof(NoArticlesSubtitle));
        public string MarkAsReadButton => Get(nameof(MarkAsReadButton));
        public string OpenInBrowser => Get(nameof(OpenInBrowser));
        public string IntervalMinutes => Get(nameof(IntervalMinutes));
        public string StatusConnected => Get(nameof(StatusConnected));
        public string StatusDisconnected => Get(nameof(StatusDisconnected));
        public string NewArticleNotificationTitle => Get(nameof(NewArticleNotificationTitle));
        public string SettingsSaved => Get(nameof(SettingsSaved));
        public string ShowUnreadOnlyLabel => Get(nameof(ShowUnreadOnlyLabel));
        public string MaxReadArticlesLabel => Get(nameof(MaxReadArticlesLabel));
        public string LimitArticlesCount => Get(nameof(LimitArticlesCount));
        public string UncategorizedGroup => Get(nameof(UncategorizedGroup));
        public string OfflineModeStatus => Get(nameof(OfflineModeStatus));
        public string SyncPendingReadsStatus => Get(nameof(SyncPendingReadsStatus));
        public string OpenLinksInBrowserLabel => Get(nameof(OpenLinksInBrowserLabel));
        public string UseGridLayoutLabel => Get(nameof(UseGridLayoutLabel));
        public string AutoStartLabel => Get(nameof(AutoStartLabel));
        public string StartMinimizedLabel => Get(nameof(StartMinimizedLabel));
        public string BackToGridButton => Get(nameof(BackToGridButton));
        public string FilterLabel => Get(nameof(FilterLabel));
        public string FilterAll => Get(nameof(FilterAll));
        public string FilterUnread => Get(nameof(FilterUnread));
        public string FilterRead => Get(nameof(FilterRead));
        public string MarkAllAsRead => Get(nameof(MarkAllAsRead));
        public string MassMarkAsRead => Get(nameof(MassMarkAsRead));
        public string MassOpen => Get(nameof(MassOpen));
        public string SelectedArticlesSuffix => Get(nameof(SelectedArticlesSuffix));
        public string SearchPlaceholder => Get(nameof(SearchPlaceholder));
        public string SelectAll => Get(nameof(SelectAll));

        // SettingsPage labels
        public string AccountExpanderHeader => Get(nameof(AccountExpanderHeader));
        public string AccountExpanderDesc => Get(nameof(AccountExpanderDesc));
        public string ServerUrlCardDesc => Get(nameof(ServerUrlCardDesc));
        public string UsernameCardDesc => Get(nameof(UsernameCardDesc));
        public string PasswordCardDesc => Get(nameof(PasswordCardDesc));
        public string SyncExpanderHeader => Get(nameof(SyncExpanderHeader));
        public string SyncExpanderDesc => Get(nameof(SyncExpanderDesc));
        public string IntervalCardDesc => Get(nameof(IntervalCardDesc));
        public string MaxReadCardDesc => Get(nameof(MaxReadCardDesc));
        public string ReadingExpanderHeader => Get(nameof(ReadingExpanderHeader));
        public string ReadingExpanderDesc => Get(nameof(ReadingExpanderDesc));
        public string OpenGraphCardDesc => Get(nameof(OpenGraphCardDesc));
        public string DefaultFilterCardHeader => Get(nameof(DefaultFilterCardHeader));
        public string DefaultFilterCardDesc => Get(nameof(DefaultFilterCardDesc));
        public string OpenInBrowserCardDesc => Get(nameof(OpenInBrowserCardDesc));
        public string SystemExpanderHeader => Get(nameof(SystemExpanderHeader));
        public string SystemExpanderDesc => Get(nameof(SystemExpanderDesc));
        public string AutoStartCardDesc => Get(nameof(AutoStartCardDesc));
        public string StartMinimizedCardDesc => Get(nameof(StartMinimizedCardDesc));
        public string LanguageCardDesc => Get(nameof(LanguageCardDesc));

        // ArticlesPage labels
        public string ListViewTooltip => Get(nameof(ListViewTooltip));
        public string GridViewTooltip => Get(nameof(GridViewTooltip));
        public string ConfirmTitle => Get(nameof(ConfirmTitle));
        public string ConfirmMarkReadContent => Get(nameof(ConfirmMarkReadContent));
        public string ConfirmOpenBrowserContent => Get(nameof(ConfirmOpenBrowserContent));
        public string YesButton => Get(nameof(YesButton));
        public string CancelButton => Get(nameof(CancelButton));
        public string ContextMarkUnread => Get(nameof(ContextMarkUnread));
        public string ContextMarkRead => Get(nameof(ContextMarkRead));
        public string ContextDeselect => Get(nameof(ContextDeselect));
        public string ContextSelect => Get(nameof(ContextSelect));

        // Tray icon labels
        public string UnreadTraySuffix => Get(nameof(UnreadTraySuffix));
        public string TrayRestore => Get(nameof(TrayRestore));
        public string TraySync => Get(nameof(TraySync));
        public string TrayExit => Get(nameof(TrayExit));
    }

    public static class LocalizationManager
    {
        private static ILocalization _current = new ResourceManagerStrings(new System.Globalization.CultureInfo("it-IT")); // Default to Italian for our user
        public static ILocalization Current
        {
            get => _current;
            private set
            {
                if (_current != value)
                {
                    _current = value;
                    LanguageChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public static string CurrentLanguageCode { get; private set; } = "it";

        public static event EventHandler? LanguageChanged;

        public static void SetLanguage(string langCode)
        {
            if (langCode.ToLower() == "it")
            {
                Current = new ResourceManagerStrings(new System.Globalization.CultureInfo("it-IT"));
                CurrentLanguageCode = "it";
            }
            else
            {
                Current = new ResourceManagerStrings(new System.Globalization.CultureInfo("en-US"));
                CurrentLanguageCode = "en";
            }
        }
    }
}
