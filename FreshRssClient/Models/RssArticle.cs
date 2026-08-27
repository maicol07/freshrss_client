using System;
using System.Text.Json.Serialization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FreshRssClient.Models
{
    public class RssArticle : ObservableObject
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

        private string _link = string.Empty;
        public string Link
        {
            get => _link;
            set => SetProperty(ref _link, value);
        }

        private DateTime _publishDate = DateTime.Now;
        public DateTime PublishDate
        {
            get => _publishDate;
            set => SetProperty(ref _publishDate, value);
        }

        private string _summary = string.Empty;
        public string Summary
        {
            get => _summary;
            set => SetProperty(ref _summary, value);
        }

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        private string _feedTitle = string.Empty;
        public string FeedTitle
        {
            get => _feedTitle;
            set => SetProperty(ref _feedTitle, value);
        }

        private string _feedId = string.Empty;
        public string FeedId
        {
            get => _feedId;
            set => SetProperty(ref _feedId, value);
        }

        private string? _feedIconUrl;
        public string? FeedIconUrl
        {
            get => _feedIconUrl;
            set
            {
                var normalized = string.IsNullOrEmpty(value) ? null : value;
                SetProperty(ref _feedIconUrl, normalized);
            }
        }

        private bool _isRead;
        public bool IsRead
        {
            get => _isRead;
            set
            {
                if (SetProperty(ref _isRead, value))
                {
                    OnPropertyChanged(nameof(TitleFontWeight));
                    OnPropertyChanged(nameof(TitleOpacity));
                    OnPropertyChanged(nameof(MarkAsReadVisibility));
                }
            }
        }

        private string? _imageUrl;
        public string? ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (SetProperty(ref _imageUrl, value))
                {
                    OnPropertyChanged(nameof(ImageVisibility));
                }
            }
        }

        // Helper properties for direct XAML bindings
        public string TitleFontWeight => IsRead ? "Normal" : "SemiBold";
        public double TitleOpacity => IsRead ? 0.6 : 1.0;
        public Microsoft.UI.Xaml.Visibility ImageVisibility => string.IsNullOrEmpty(ImageUrl) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        public Microsoft.UI.Xaml.Visibility MarkAsReadVisibility => IsRead ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        private ICommand? _markAsReadCommand;
        [JsonIgnore]
        public ICommand? MarkAsReadCommand
        {
            get => _markAsReadCommand;
            set => SetProperty(ref _markAsReadCommand, value);
        }

        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    OnSelectionToggled?.Invoke(this);
                }
            }
        }

        [JsonIgnore]
        public Action<RssArticle>? OnSelectionToggled { get; set; }
    }
}
