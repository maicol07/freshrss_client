using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using FreshRssClient.Services;
using FreshRssClient.Helpers;

namespace FreshRssClient.Views
{
    public sealed partial class ArticleDetailPage : Page
    {
        private RssArticle? _article;

        public ArticleDetailPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is RssArticle article)
            {
                _article = article;
                DataContext = article;

                DetailTitle.Text = article.Title;
                FeedMeta.Text = article.FeedTitle;
                DateMeta.Text = article.PublishDate.ToString("f");
                BodyText.Text = article.Summary;

                if (!string.IsNullOrEmpty(article.FeedIconUrl))
                {
                    try { DetailFeedIcon.Source = new BitmapImage(new Uri(article.FeedIconUrl)); } catch { }
                    DetailFeedIcon.Visibility = Visibility.Visible;
                }
                else
                {
                    DetailFeedIcon.Visibility = Visibility.Collapsed;
                }

                if (!string.IsNullOrEmpty(article.ImageUrl))
                {
                    ArticleImage.Source = ImageLoader.Create(article.ImageUrl);
                    ArticleImageBorder.Visibility = Visibility.Visible;
                }
                else
                {
                    ArticleImageBorder.Visibility = Visibility.Collapsed;
                }

                OpenBrowserButton.Content = LocalizationManager.Current.OpenInBrowser;
            }
        }

        private async void OnOpenInBrowserClicked(object sender, RoutedEventArgs e)
        {
            if (_article != null && !string.IsNullOrEmpty(_article.Link))
            {
                try
                {
                    if (WebUri.TryCreate(_article.Link, out var uri))
                    {
                        await Windows.System.Launcher.LaunchUriAsync(uri);
                    }
                }
                catch { }
            }
        }
    }
}
