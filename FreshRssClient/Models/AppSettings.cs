namespace FreshRssClient.Models
{
    public class AppSettings
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public int SyncInterval { get; set; } = 15;
        public bool ShowUnreadOnly { get; set; } = false;
        public string ArticleFilter { get; set; } = "All";
        public int MaxReadArticles { get; set; } = 50;
        public string Language { get; set; } = "it";
        public bool UseGridLayout { get; set; }
        public bool OpenLinksInBrowser { get; set; }
        public bool FetchMissingImagesFromWeb { get; set; } = true;
        public bool AutoStartWithWindows { get; set; }
        public bool StartMinimizedInTray { get; set; }
    }
}
