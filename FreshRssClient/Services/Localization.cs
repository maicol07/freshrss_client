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
    }

    public class EnglishStrings : ILocalization
    {
        public string AppTitle => "FreshRSS Client";
        public string FeedsTab => "Articles";
        public string SettingsTab => "Settings";
        public string ServerUrlLabel => "FreshRSS Server URL (ends with greader.php)";
        public string UsernameLabel => "Username";
        public string ApiPasswordLabel => "API Password (configured in FreshRSS profile)";
        public string UpdateIntervalLabel => "Sync Interval (Minutes)";
        public string EnableOpenGraphLabel => "Enable OpenGraph (scrape rich article text & images)";
        public string LanguageLabel => "Language";
        public string SaveButton => "Save Settings";
        public string SavedSuccess => "Settings saved successfully!";
        public string SyncNowButton => "Sync Now";
        public string SyncingStatus => "Synchronizing feeds...";
        public string SyncSuccess => "Synced successfully!";
        public string SyncError => "Synchronization failed: {0}";
        public string UnreadArticlesHeader => "Unread Articles";
        public string NoArticles => "No unread articles. Excellent!";
        public string NoArticlesSubtitle => "You're all caught up! Enjoy your day.";
        public string MarkAsReadButton => "Mark as Read";
        public string OpenInBrowser => "Read full article in browser";
        public string IntervalMinutes => "{0} minutes";
        public string StatusConnected => "Connected";
        public string StatusDisconnected => "Disconnected (Check settings)";
        public string NewArticleNotificationTitle => "New article from {0}";
        public string SettingsSaved => "Settings saved successfully";
        public string ShowUnreadOnlyLabel => "Show unread articles only";
        public string MaxReadArticlesLabel => "Max read articles to sync";
        public string LimitArticlesCount => "{0} articles";
        public string UncategorizedGroup => "Uncategorized";
        public string OfflineModeStatus => "Offline mode active (using cached data)";
        public string SyncPendingReadsStatus => "Syncing offline read status updates...";
        public string OpenLinksInBrowserLabel => "Open articles directly in browser";
        public string UseGridLayoutLabel => "Use grid layout";
        public string AutoStartLabel => "Start with Windows";
        public string StartMinimizedLabel => "Start minimized in system tray";
        public string BackToGridButton => "Back to Grid";
        public string FilterLabel => "Filter";
        public string FilterAll => "All articles";
        public string FilterUnread => "Unread only";
        public string FilterRead => "Read only";
        public string MarkAllAsRead => "Mark all as read";
        public string MassMarkAsRead => "Mark read";
        public string MassOpen => "Open selected";
        public string SelectedArticlesSuffix => "selected";
        public string SearchPlaceholder => "Search...";
        public string SelectAll => "Select / Deselect all";
    }

    public class ItalianStrings : ILocalization
    {
        public string AppTitle => "Lettore FreshRSS";
        public string FeedsTab => "Articoli";
        public string SettingsTab => "Impostazioni";
        public string ServerUrlLabel => "URL Server FreshRSS (deve terminare con greader.php)";
        public string UsernameLabel => "Nome Utente";
        public string ApiPasswordLabel => "Password API (configurata nel profilo FreshRSS)";
        public string UpdateIntervalLabel => "Intervallo di Sincronizzazione (Minuti)";
        public string EnableOpenGraphLabel => "Abilita OpenGraph (scarica testo ricco e immagini copertina)";
        public string LanguageLabel => "Lingua";
        public string SaveButton => "Salva Impostazioni";
        public string SavedSuccess => "Impostazioni salvate con successo!";
        public string SyncNowButton => "Sincronizza Ora";
        public string SyncingStatus => "Sincronizzazione feed...";
        public string SyncSuccess => "Sincronizzazione completata!";
        public string SyncError => "Errore di sincronizzazione: {0}";
        public string UnreadArticlesHeader => "Articoli non letti";
        public string NoArticles => "Nessun articolo da leggere. Ottimo!";
        public string NoArticlesSubtitle => "Hai letto tutto! Goditi la giornata.";
        public string MarkAsReadButton => "Segna come letto";
        public string OpenInBrowser => "Leggi l'articolo completo nel browser";
        public string IntervalMinutes => "{0} minuti";
        public string StatusConnected => "Connesso";
        public string StatusDisconnected => "Disconnesso (Verifica impostazioni)";
        public string NewArticleNotificationTitle => "Nuovo articolo da {0}";
        public string SettingsSaved => "Impostazioni salvate con successo";
        public string ShowUnreadOnlyLabel => "Mostra solo articoli non letti";
        public string MaxReadArticlesLabel => "Limite articoli letti da sincronizzare";
        public string LimitArticlesCount => "{0} articoli";
        public string UncategorizedGroup => "Senza categoria";
        public string OfflineModeStatus => "Modalità offline attiva (dati caricati dalla cache)";
        public string SyncPendingReadsStatus => "Sincronizzazione articoli letti offline...";
        public string OpenLinksInBrowserLabel => "Apri articoli direttamente nel browser";
        public string UseGridLayoutLabel => "Usa layout a griglia";
        public string AutoStartLabel => "Avvia con Windows";
        public string StartMinimizedLabel => "Avvia ridotto nella tray";
        public string BackToGridButton => "Torna alla griglia";
        public string FilterLabel => "Filtra";
        public string FilterAll => "Tutti gli articoli";
        public string FilterUnread => "Solo non letti";
        public string FilterRead => "Solo letti";
        public string MarkAllAsRead => "Segna tutto come letto";
        public string MassMarkAsRead => "Segna come letti";
        public string MassOpen => "Apri selezionati";
        public string SelectedArticlesSuffix => "selezionati";
        public string SearchPlaceholder => "Cerca...";
        public string SelectAll => "Seleziona / Deseleziona tutto";
    }

    public static class LocalizationManager
    {
        private static ILocalization _current = new ItalianStrings(); // Default to Italian for our user
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
                Current = new ItalianStrings();
                CurrentLanguageCode = "it";
            }
            else
            {
                Current = new EnglishStrings();
                CurrentLanguageCode = "en";
            }
        }
    }
}
