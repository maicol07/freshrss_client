using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FreshRssClient.Models
{
    public class RssCategory : ObservableObject
    {
        private string _id = string.Empty;
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private int _unreadCount;
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (SetProperty(ref _unreadCount, value))
                {
                    OnPropertyChanged(nameof(HasUnread));
                }
            }
        }

        public bool HasUnread => UnreadCount > 0;

        private ObservableCollection<RssFeed> _feeds = new();
        public ObservableCollection<RssFeed> Feeds
        {
            get => _feeds;
            set => SetProperty(ref _feeds, value);
        }
    }
}
