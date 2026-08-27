using System.Collections.Generic;

namespace FreshRssClient.Models
{
    public class OfflineCache
    {
        public List<RssCategory> Categories { get; set; } = new();
        public List<RssFeed> Feeds { get; set; } = new();
        public Dictionary<string, List<RssArticle>> ArticlesByStream { get; set; } = new();
    }
}
