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
        private RssArticle? _contextArticle;

        public ArticlesPage()
        {
            this.InitializeComponent();
            this.Unloaded += OnUnloaded;

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
            ProgressIndicator.Visibility = _viewModel.IsSyncing ? Visibility.Visible : Visibility.Collapsed;
            SelectedCountText.Text = _viewModel.SelectedArticlesCountText;
            
            // Initialize checked states of filter items
            ShowUnreadItem.IsChecked = _viewModel.ArticleFilter == "Unread";
            ShowReadItem.IsChecked = _viewModel.ArticleFilter == "Read";
            ShowAllItem.IsChecked = _viewModel.ArticleFilter == "All";

            // Set bindings for static elements
            RefreshBtn.Command = _viewModel.SyncCommand;

            UpdateFilterVisuals();
            UpdateLocalizations();

            // Subscribe to VM PropertyChanged events
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Subscribe to Articles collection changes to dynamically toggle empty state
            _viewModel.Articles.CollectionChanged += OnArticlesCollectionChanged;

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

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Articles.CollectionChanged -= OnArticlesCollectionChanged;
            }
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            this.Unloaded -= OnUnloaded;
        }

        private void OnArticlesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateEmptyState();
            });
        }

        private void UpdateEmptyState()
        {
            if (_viewModel == null) return;
            
            bool isEmpty = _viewModel.Articles.Count == 0;
            EmptyStateView.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

            if (isEmpty)
            {
                ArticlesListView.Visibility = Visibility.Collapsed;
                ArticlesGridView.Visibility = Visibility.Collapsed;
            }
            else
            {
                ArticlesListView.Visibility = _viewModel.UseGridLayout ? Visibility.Collapsed : Visibility.Visible;
                ArticlesGridView.Visibility = _viewModel.UseGridLayout ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateLocalizations()
        {
            if (_viewModel == null) return;

            ShowUnreadItem.Text = LocalizationManager.Current.FilterUnread;
            ShowReadItem.Text = LocalizationManager.Current.FilterRead;
            ShowAllItem.Text = LocalizationManager.Current.FilterAll;

            ToolTipService.SetToolTip(RefreshBtn, LocalizationManager.Current.SyncNowButton);
            ToolTipService.SetToolTip(SelectAllBtn, LocalizationManager.Current.SelectAll);
            
            OpenBrowserButton.Content = LocalizationManager.Current.OpenInBrowser;
            
            MassMarkReadText.Text = LocalizationManager.Current.MassMarkAsRead;
            MassOpenText.Text = LocalizationManager.Current.MassOpen;

            EmptyStateHeader.Text = LocalizationManager.Current.NoArticles;
            EmptyStateSubtitle.Text = LocalizationManager.Current.NoArticlesSubtitle;
        }

        private void UpdateFilterVisuals()
        {
            if (_viewModel == null) return;

            if (_viewModel.ArticleFilter != "All")
            {
                FilterBtn.Background = (Brush)Application.Current.Resources["SystemAccentColorBrush"];
                FilterBtn.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);

                // Override hover and pressed states when active so background does not disappear
                FilterBtn.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight1"]);
                FilterBtn.Resources["ButtonBackgroundPressed"] = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColorDark1"]);
                FilterBtn.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.White);
                FilterBtn.Resources["ButtonForegroundPressed"] = new SolidColorBrush(Microsoft.UI.Colors.White);

                string filterName = _viewModel.ArticleFilter == "Unread" 
                    ? LocalizationManager.Current.FilterUnread 
                    : LocalizationManager.Current.FilterRead;
                ToolTipService.SetToolTip(FilterBtn, $"{LocalizationManager.Current.FilterLabel} ({filterName})");
            }
            else
            {
                FilterBtn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                FilterBtn.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

                // Remove overrides for transparent default look
                FilterBtn.Resources.Remove("ButtonBackgroundPointerOver");
                FilterBtn.Resources.Remove("ButtonBackgroundPressed");
                FilterBtn.Resources.Remove("ButtonForegroundPointerOver");
                FilterBtn.Resources.Remove("ButtonForegroundPressed");

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
                VerticalDivider.Visibility = Visibility.Collapsed;

                if (_viewModel.SelectedArticle == null)
                {
                    LeftGrid.Visibility = Visibility.Visible;
                    Grid.SetColumn(LeftGrid, 0);
                    Grid.SetColumnSpan(LeftGrid, 2);
                    
                    RightScroll.Visibility = Visibility.Collapsed;
                    DetailFrame.Visibility = Visibility.Collapsed;
                    DetailFrame.Content = null;
                }
                else
                {
                    LeftGrid.Visibility = Visibility.Collapsed;
                    RightScroll.Visibility = Visibility.Collapsed;
                    DetailFrame.Visibility = Visibility.Visible;
                    Grid.SetColumn(DetailFrame, 0);
                    Grid.SetColumnSpan(DetailFrame, 2);

                    if (DetailFrame.Content is ArticleDetailPage detailPage && DetailFrame.DataContext == _viewModel.SelectedArticle)
                    {
                        // Already navigated
                    }
                    else
                    {
                        DetailFrame.DataContext = _viewModel.SelectedArticle;
                        DetailFrame.Navigate(typeof(ArticleDetailPage), _viewModel.SelectedArticle, new DrillInNavigationTransitionInfo());
                    }
                }
            }
            else
            {
                // Split list layout
                VerticalDivider.Visibility = Visibility.Visible;

                LeftGrid.Visibility = Visibility.Visible;
                Grid.SetColumn(LeftGrid, 0);
                Grid.SetColumnSpan(LeftGrid, 1);

                DetailFrame.Visibility = Visibility.Collapsed;
                DetailFrame.Content = null;

                RightScroll.Visibility = _viewModel.SelectedArticle != null && !_viewModel.IsMultiSelectMode ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetColumn(RightScroll, 1);
                Grid.SetColumnSpan(RightScroll, 1);

                LeftColumnDefinition.Width = new GridLength(350);
                RightColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
            }

            // Update layout button icon and tooltip
            LayoutBtnIcon.Glyph = useGrid ? "\uE8FD" : "\uE80A";
            ToolTipService.SetToolTip(LayoutBtn, useGrid ? 
                (LocalizationManager.CurrentLanguageCode == "it" ? "Visualizzazione elenco" : "List view") :
                (LocalizationManager.CurrentLanguageCode == "it" ? "Visualizzazione griglia" : "Grid view"));

            // Toggle empty state / lists visibility
            UpdateEmptyState();
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

                bool isMulti = isModifierPressed || items.Count > 1;

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
                    if (selectedList.Count > 0)
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
            if (_viewModel == null) return;

            var count = _viewModel.SelectedArticles.Count;
            var dialog = new ContentDialog
            {
                Title = LocalizationManager.CurrentLanguageCode == "it" ? "Conferma" : "Confirm",
                Content = string.Format(
                    LocalizationManager.CurrentLanguageCode == "it"
                        ? "Segnare {0} articoli come letti?"
                        : "Mark {0} articles as read?",
                    count),
                PrimaryButtonText = LocalizationManager.CurrentLanguageCode == "it" ? "Sì" : "Yes",
                CloseButtonText = LocalizationManager.CurrentLanguageCode == "it" ? "Annulla" : "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await _viewModel.MarkSelectedAsReadAsync();
        }

        private async void OnMassOpenClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            var count = _viewModel.SelectedArticles.Count;
            var dialog = new ContentDialog
            {
                Title = LocalizationManager.CurrentLanguageCode == "it" ? "Conferma" : "Confirm",
                Content = string.Format(
                    LocalizationManager.CurrentLanguageCode == "it"
                        ? "Aprire {0} articoli nel browser?"
                        : "Open {0} articles in browser?",
                    count),
                PrimaryButtonText = LocalizationManager.CurrentLanguageCode == "it" ? "Sì" : "Yes",
                CloseButtonText = LocalizationManager.CurrentLanguageCode == "it" ? "Annulla" : "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await _viewModel.OpenSelectedInBrowserAsync();
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

        private void OnSelectAllClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.ToggleSelectAll();
            
            // Synchronize selection in ListView or GridView
            _isUpdatingSelection = true;
            try
            {
                var selector = _viewModel.UseGridLayout ? (ListViewBase)ArticlesGridView : (ListViewBase)ArticlesListView;
                selector.SelectedItems.Clear();
                if (_viewModel.IsMultiSelectMode)
                {
                    foreach (var article in _viewModel.Articles)
                    {
                        selector.SelectedItems.Add(article);
                    }
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        private void OnItemPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_viewModel == null) return;

            var properties = e.GetCurrentPoint(sender as UIElement).Properties;
            if (properties.IsMiddleButtonPressed)
            {
                e.Handled = true;

                // Toggle selection for this article
                var element = sender as FrameworkElement;
                if (element?.DataContext is RssArticle article)
                {
                    article.IsSelected = !article.IsSelected;

                    // Build list of selected articles
                    var selectedList = new List<RssArticle>();
                    foreach (var a in _viewModel.Articles)
                    {
                        if (a.IsSelected)
                        {
                            selectedList.Add(a);
                        }
                    }

                    _isUpdatingSelection = true;
                    try
                    {
                        if (selectedList.Count > 0)
                        {
                            _viewModel.IsMultiSelectMode = true;
                            _viewModel.SetSelectedArticles(selectedList);

                            // Synchronize SelectedItems of the active list/grid control
                            var selector = _viewModel.UseGridLayout ? (ListViewBase)ArticlesGridView : (ListViewBase)ArticlesListView;
                            selector.SelectedItems.Clear();
                            foreach (var a in selectedList)
                            {
                                selector.SelectedItems.Add(a);
                            }
                        }
                        else
                        {
                            _viewModel.IsMultiSelectMode = false;
                            _viewModel.SelectedArticle = null;
                            ArticlesListView.SelectedItems.Clear();
                            ArticlesGridView.SelectedItems.Clear();
                        }
                    }
                    finally
                    {
                        _isUpdatingSelection = false;
                    }

                    // Explicitly trigger apply visual updates
                    ApplyLayoutMode();
                }
            }
        }

        private void OnArticleContextFlyoutOpening(object sender, object e)
        {
            if (_viewModel == null) return;

            if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement element && element.DataContext is RssArticle article)
            {
                _contextArticle = article;

                // Update ContextToggleReadItem
                ContextToggleReadItem.Text = article.IsRead 
                    ? (LocalizationManager.CurrentLanguageCode == "it" ? "Segna come da leggere" : "Mark as unread")
                    : (LocalizationManager.CurrentLanguageCode == "it" ? "Segna come letto" : "Mark as read");
                
                if (ContextToggleReadItem.Icon is SymbolIcon symbolIcon)
                {
                    symbolIcon.Symbol = article.IsRead ? Symbol.Document : Symbol.Read;
                }

                // Update ContextOpenBrowserItem text
                ContextOpenBrowserItem.Text = LocalizationManager.Current.OpenInBrowser;

                // Update ContextSelectItem text
                ContextSelectItem.Text = article.IsSelected
                    ? (LocalizationManager.CurrentLanguageCode == "it" ? "Deseleziona" : "Deselect")
                    : (LocalizationManager.CurrentLanguageCode == "it" ? "Seleziona" : "Select");
            }
        }

        private async void OnContextToggleReadClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || _contextArticle == null) return;

            if (_contextArticle.IsRead)
            {
                await _viewModel.MarkArticleAsUnreadAsync(_contextArticle);
            }
            else
            {
                await _viewModel.MarkArticleAsReadAsync(_contextArticle);
            }
        }

        private async void OnContextOpenInBrowserClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || _contextArticle == null || string.IsNullOrEmpty(_contextArticle.Link)) return;

            try
            {
                var uri = new Uri(_contextArticle.Link);
                await Launcher.LaunchUriAsync(uri);
                
                await _viewModel.MarkArticleAsReadAsync(_contextArticle);
            }
            catch { }
        }

        private void OnContextSelectClicked(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || _contextArticle == null) return;

            _contextArticle.IsSelected = !_contextArticle.IsSelected;

            // Sync SelectedArticles
            var selectedList = new List<RssArticle>();
            foreach (var a in _viewModel.Articles)
            {
                if (a.IsSelected)
                {
                    selectedList.Add(a);
                }
            }

            _isUpdatingSelection = true;
            try
            {
                if (selectedList.Count > 0)
                {
                    _viewModel.IsMultiSelectMode = true;
                    _viewModel.SetSelectedArticles(selectedList);

                    var selector = _viewModel.UseGridLayout ? (ListViewBase)ArticlesGridView : (ListViewBase)ArticlesListView;
                    selector.SelectedItems.Clear();
                    foreach (var a in selectedList)
                    {
                        selector.SelectedItems.Add(a);
                    }
                }
                else
                {
                    _viewModel.IsMultiSelectMode = false;
                    _viewModel.SelectedArticle = null;
                    ArticlesListView.SelectedItems.Clear();
                    ArticlesGridView.SelectedItems.Clear();
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            ApplyLayoutMode();
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
