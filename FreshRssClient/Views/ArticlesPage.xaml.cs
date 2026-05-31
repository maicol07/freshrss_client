using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using FreshRssClient.ViewModels;
using FreshRssClient.Services;
using FreshRssClient.Helpers;

namespace FreshRssClient.Views
{
    public sealed partial class ArticlesPage : Page
    {
        private MainViewModel? _viewModel;
        private bool _isUpdatingSelection = false;

        public ArticlesPage()
        {
            this.InitializeComponent();

            // Set grid view styling using a safe, standard style that preserves native Fluent templates
            ArticlesGridView.ItemContainerStyle = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
            <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='GridViewItem'>
                <Setter Property='Margin' Value='6'/>
                <Setter Property='Padding' Value='0'/>
                <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                <Setter Property='VerticalContentAlignment' Value='Stretch'/>
            </Style>");

            // Fluid staggering entrance animations
            ArticlesListView.ItemContainerTransitions = new TransitionCollection
            {
                new EntranceThemeTransition { FromVerticalOffset = 24, IsStaggeringEnabled = true },
                new AddDeleteThemeTransition(),
                new ReorderThemeTransition()
            };
            ArticlesGridView.ItemContainerTransitions = new TransitionCollection
            {
                new EntranceThemeTransition { FromVerticalOffset = 24, IsStaggeringEnabled = true },
                new AddDeleteThemeTransition(),
                new ReorderThemeTransition()
            };
        }

        public void Initialize(MainViewModel viewModel)
        {
            _viewModel = viewModel;

            // Bind list items manually in code-behind to match modern MVVM decoupling
            ArticlesListView.ItemsSource = _viewModel.Articles;
            ArticlesGridView.ItemsSource = _viewModel.Articles;

            // Initialize texts
            TitleText.Text = _viewModel.UnreadCountHeaderText;
            SyncStatusText.Text = _viewModel.SyncStatusText;
            ProgressIndicator.IsActive = _viewModel.IsSyncing;
            ProgressIndicator.Visibility = _viewModel.IsSyncing ? Visibility.Visible : Visibility.Collapsed;
            SelectedCountText.Text = _viewModel.SelectedArticlesCountText;
            
            // Set bindings for static elements
            RefreshBtn.Command = _viewModel.SyncCommand;

            UpdateFilterVisuals();
            UpdateLocalizations();

            // Subscribe to VM PropertyChanged events
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Subscribe to language changes
            LocalizationManager.LanguageChanged += OnLanguageChanged;

            // Initial visual state application
            ApplyLayoutMode();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null) return;

            this.DispatcherQueue.TryEnqueue(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(MainViewModel.UnreadCountHeaderText):
                        TitleText.Text = _viewModel.UnreadCountHeaderText;
                        break;

                    case nameof(MainViewModel.SyncStatusText):
                        SyncStatusText.Text = _viewModel.SyncStatusText;
                        break;

                    case nameof(MainViewModel.IsSyncing):
                        ProgressIndicator.IsActive = _viewModel.IsSyncing;
                        ProgressIndicator.Visibility = _viewModel.IsSyncing ? Visibility.Visible : Visibility.Collapsed;
                        break;

                    case nameof(MainViewModel.SelectedArticlesCountText):
                        SelectedCountText.Text = _viewModel.SelectedArticlesCountText;
                        break;

                    case nameof(MainViewModel.ArticleFilter):
                        ShowUnreadItem.IsChecked = _viewModel.ArticleFilter == "Unread";
                        ShowReadItem.IsChecked = _viewModel.ArticleFilter == "Read";
                        ShowAllItem.IsChecked = _viewModel.ArticleFilter == "All";
                        UpdateFilterVisuals();
                        break;

                    case nameof(MainViewModel.SelectedArticle):
                        SyncSelectionToUI();
                        ApplyLayoutMode();
                        break;

                    case nameof(MainViewModel.UseGridLayout):
                        ApplyLayoutMode();
                        break;

                    case nameof(MainViewModel.IsMultiSelectMode):
                        UpdateMultiSelectMode();
                        break;
                }
            });
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateLocalizations();
                UpdateFilterVisuals();
            });
        }

        private void UpdateLocalizations()
        {
            if (_viewModel == null) return;

            ShowUnreadItem.Text = LocalizationManager.Current.FilterUnread;
            ShowReadItem.Text = LocalizationManager.Current.FilterRead;
            ShowAllItem.Text = LocalizationManager.Current.FilterAll;

            ToolTipService.SetToolTip(RefreshBtn, LocalizationManager.Current.SyncNowButton);
            ToolTipService.SetToolTip(MarkAllReadBtn, LocalizationManager.Current.MarkAllAsRead);
            
            OpenBrowserButton.Content = LocalizationManager.Current.OpenInBrowser;
            
            MassMarkReadText.Text = LocalizationManager.Current.MassMarkAsRead;
            MassOpenText.Text = LocalizationManager.Current.MassOpen;
        }

        private void UpdateFilterVisuals()
        {
            if (_viewModel == null) return;

            if (_viewModel.ArticleFilter != "All")
            {
                FilterBtn.Background = (Brush)Application.Current.Resources["SystemAccentColorBrush"];
                FilterBtn.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                string filterName = _viewModel.ArticleFilter == "Unread" 
                    ? LocalizationManager.Current.FilterUnread 
                    : LocalizationManager.Current.FilterRead;
                ToolTipService.SetToolTip(FilterBtn, $"{LocalizationManager.Current.FilterLabel} ({filterName})");
            }
            else
            {
                FilterBtn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                FilterBtn.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
                ToolTipService.SetToolTip(FilterBtn, LocalizationManager.Current.FilterLabel);
            }
        }

        public void ApplyLayoutMode()
        {
            if (_viewModel == null) return;

            bool useGrid = _viewModel.UseGridLayout;

            if (useGrid)
            {
                // Grid layout
                ArticlesListView.Visibility = Visibility.Collapsed;
                ArticlesGridView.Visibility = Visibility.Visible;
                VerticalDivider.Visibility = Visibility.Collapsed;

                if (_viewModel.SelectedArticle == null)
                {
                    LeftGrid.Visibility = Visibility.Visible;
                    Grid.SetColumn(LeftGrid, 0);
                    Grid.SetColumnSpan(LeftGrid, 2);
                    
                    RightScroll.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LeftGrid.Visibility = Visibility.Collapsed;
                    
                    RightScroll.Visibility = Visibility.Visible;
                    Grid.SetColumn(RightScroll, 0);
                    Grid.SetColumnSpan(RightScroll, 2);
                }
            }
            else
            {
                // Split list layout
                ArticlesListView.Visibility = Visibility.Visible;
                ArticlesGridView.Visibility = Visibility.Collapsed;
                VerticalDivider.Visibility = Visibility.Visible;

                LeftGrid.Visibility = Visibility.Visible;
                Grid.SetColumn(LeftGrid, 0);
                Grid.SetColumnSpan(LeftGrid, 1);

                RightScroll.Visibility = _viewModel.SelectedArticle != null && !_viewModel.IsMultiSelectMode ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetColumn(RightScroll, 1);
                Grid.SetColumnSpan(RightScroll, 1);

                LeftColumnDefinition.Width = new GridLength(350);
                RightColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
            }

            // Update layout button icon and tooltip
            LayoutBtnIcon.Glyph = useGrid ? "\uE292" : "\uE118";
            ToolTipService.SetToolTip(LayoutBtn, useGrid ? 
                (LocalizationManager.CurrentLanguageCode == "it" ? "Visualizzazione elenco" : "List view") :
                (LocalizationManager.CurrentLanguageCode == "it" ? "Visualizzazione griglia" : "Grid view"));
        }

        private void UpdateMultiSelectMode()
        {
            if (_viewModel == null) return;

            bool multi = _viewModel.IsMultiSelectMode;

            if (!multi)
            {
                if (_viewModel.SelectedArticle == null)
                {
                    ArticlesListView.SelectedItems.Clear();
                    ArticlesGridView.SelectedItems.Clear();
                    ArticlesListView.SelectedItem = null;
                    ArticlesGridView.SelectedItem = null;
                }
                else
                {
                    ArticlesListView.SelectedItem = _viewModel.SelectedArticle;
                    ArticlesGridView.SelectedItem = _viewModel.SelectedArticle;
                }
            }

            MassMenuBar.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SyncSelectionToUI()
        {
            if (_viewModel == null || _isUpdatingSelection) return;

            _isUpdatingSelection = true;
            try
            {
                var article = _viewModel.SelectedArticle;

                if (article != null && !_viewModel.IsMultiSelectMode)
                {
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
                        try { ArticleImage.Source = new BitmapImage(new Uri(article.ImageUrl)); } catch { }
                        ArticleImageBorder.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ArticleImageBorder.Visibility = Visibility.Collapsed;
                    }

                    ArticlesListView.SelectedItem = article;
                    ArticlesGridView.SelectedItem = article;

                    RightScroll.Visibility = Visibility.Visible;
                }
                else
                {
                    RightScroll.Visibility = Visibility.Collapsed;
                    
                    if (!_viewModel.IsMultiSelectMode)
                    {
                        ArticlesListView.SelectedItem = null;
                        ArticlesGridView.SelectedItem = null;
                    }
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        private void UpdateReaderAndSelection()
        {
            if (_viewModel == null || _isUpdatingSelection) return;
            _isUpdatingSelection = true;

            try
            {
                var items = _viewModel.UseGridLayout ? ArticlesGridView.SelectedItems : ArticlesListView.SelectedItems;

                // Check keyboard modifiers to support Ctrl/Shift selection on first click
                var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
                var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
                bool isCtrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                bool isShiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                bool isModifierPressed = isCtrlPressed || isShiftPressed;

                bool isMulti = isModifierPressed || items.Count > 1 || _viewModel.IsHeaderToggleActive;

                if (isMulti)
                {
                    var selectedList = new List<RssArticle>();
                    foreach (var item in items)
                    {
                        if (item is RssArticle article)
                        {
                            selectedList.Add(article);
                        }
                    }
                    if (selectedList.Count > 0 || _viewModel.IsHeaderToggleActive)
                    {
                        _viewModel.IsMultiSelectMode = true;
                        _viewModel.SetSelectedArticles(selectedList);
                    }
                    else
                    {
                        _viewModel.IsMultiSelectMode = false;
                        _viewModel.SelectedArticle = null;
                    }
                }
                else if (items.Count == 1)
                {
                    var article = items[0] as RssArticle;
                    if (article != null)
                    {
                        if (_viewModel.OpenLinksInBrowser)
                        {
                            // Clear UI selection synchronously to prevent reentrancy and infinite loops
                            ArticlesListView.SelectedItems.Clear();
                            ArticlesGridView.SelectedItems.Clear();
                            ArticlesListView.SelectedItem = null;
                            ArticlesGridView.SelectedItem = null;
                            
                            _viewModel.IsMultiSelectMode = false;
                            _viewModel.SelectedArticle = null;

                            // Open in browser directly
                            SafeFireAndForget.Run(() => _viewModel.MarkAsReadAndOpenBrowserAsync(article));
                        }
                        else
                        {
                            _viewModel.IsMultiSelectMode = false;
                            _viewModel.SelectedArticle = article;
                        }
                    }
                }
                else
                {
                    _viewModel.IsMultiSelectMode = false;
                    _viewModel.SelectedArticle = null;
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            // Sync visual detail state based on selection change
            SyncSelectionToUI();
            ApplyLayoutMode();
        }

        private void OnArticlesListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateReaderAndSelection();
        }

        private void OnArticlesGridViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateReaderAndSelection();
        }

        private void OnFilterUnreadClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null) _viewModel.ArticleFilter = "Unread";
        }

        private void OnFilterReadClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null) _viewModel.ArticleFilter = "Read";
        }

        private void OnFilterAllClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null) _viewModel.ArticleFilter = "All";
        }

        private async void OnMarkAllReadClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null) await _viewModel.MarkAllAsReadInActiveStreamAsync();
        }

        private void OnLayoutClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null) _viewModel.UseGridLayout = !_viewModel.UseGridLayout;
        }

        private async void OnMassMarkReadClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null) await _viewModel.MarkSelectedAsReadAsync();
        }

        private async void OnMassOpenClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null) await _viewModel.OpenSelectedInBrowserAsync();
        }

        private void OnBackClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null) _viewModel.SelectedArticle = null;
        }

        private async void OnOpenInBrowserClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.SelectedArticle != null && !string.IsNullOrEmpty(_viewModel.SelectedArticle.Link))
            {
                try
                {
                    var uri = new Uri(_viewModel.SelectedArticle.Link);
                    await Launcher.LaunchUriAsync(uri);
                }
                catch
                {
                    // Ignore launch failures
                }
            }
        }
    }

    public class StringToImageSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string url && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    return new BitmapImage(new Uri(url));
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
