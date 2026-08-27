using CommunityToolkit.Mvvm.ComponentModel;

namespace FreshRssClient.Models
{
    public class RssFeed : ObservableObject
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

        private string _htmlUrl = string.Empty;
        public string HtmlUrl
        {
            get => _htmlUrl;
            set => SetProperty(ref _htmlUrl, value);
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

        private string? _iconUrl;
        public string? IconUrl
        {
            get => _iconUrl;
            set
            {
                var normalized = string.IsNullOrEmpty(value) ? null : value;
                SetProperty(ref _iconUrl, normalized);
            }
        }
    }
}
