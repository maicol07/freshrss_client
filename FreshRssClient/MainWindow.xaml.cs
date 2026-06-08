using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using FreshRssClient.Helpers;
using FreshRssClient.ViewModels;
using FreshRssClient.Services;
using FreshRssClient.Views;

namespace FreshRssClient
{
    public sealed partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly TrayIconHelper _trayIconHelper;
        public bool IsExiting { get; set; } = false;
        public bool ShouldStartMinimized { get; private set; } = false;

        private ArticlesPage? _articlesPage;
        private SettingsPage? _settingsPage;
        private bool _isRebuildingNavigation = false;
        private readonly List<Action> _menuEventCleanupActions = new();

        public MainWindow()
        {
            this.InitializeComponent();

            try
            {
                this.SystemBackdrop = new MicaBackdrop();
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);
                AppWindow.SetIcon("Assets/AppIcon.ico");

                _viewModel = new MainViewModel();

                _trayIconHelper = new TrayIconHelper(this, () =>
                {
                    if (_viewModel.SyncCommand.CanExecute(null))
                        _viewModel.SyncCommand.Execute(null);
                });

                _viewModel.RegisterTrayIconHelper(_trayIconHelper);

                this.AppWindow.Closing += (sender, args) =>
                {
                    if (IsExiting)
                    {
                        _trayIconHelper.Dispose();
                        _viewModel.Dispose();
                        return;
                    }
                    args.Cancel = true;
                    _trayIconHelper.MinimizeToTray();
                };

                string[] cmdArgs = Environment.GetCommandLineArgs();
                
                bool isStartupTask = false;
                try
                {
                    var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                    if (activatedArgs != null && activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask)
                    {
                        isStartupTask = true;
                    }
                }
                catch { }

                ShouldStartMinimized = cmdArgs.Contains("--minimized", StringComparer.OrdinalIgnoreCase) ||
                                       cmdArgs.Contains("-minimized", StringComparer.OrdinalIgnoreCase) ||
                                       (isStartupTask && _viewModel.StartMinimizedInTray);

                if (ShouldStartMinimized)
                {
                    _trayIconHelper.MinimizeToTray();
                }

                ConfigureSearch();
                BuildSubPanels();

                _viewModel.NavigationStructureChanged += (s, ev) =>
                {
                    this.DispatcherQueue.TryEnqueue(() => RebuildNavigationMenu());
                };
                RebuildNavigationMenu();

                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                AppNavigationView.IsBackEnabled = false;
                AppTitleBar.IsBackButtonEnabled = false;
            }
            catch (Exception ex)
            {
                try
                {
                    var localFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreshRssClient");
                    System.IO.Directory.CreateDirectory(localFolder);
                    var logPath = System.IO.Path.Combine(localFolder, "crash_log.txt");
                    System.IO.File.WriteAllText(logPath, ex.ToString());
                }
                catch { }
                throw;
            }
        }

        private void OnNavigationBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (_viewModel?.SelectedArticle != null)
                _viewModel.SelectedArticle = null;
        }

        private void OnTitleBarBackRequested(TitleBar sender, object args)
        {
            if (_viewModel?.SelectedArticle != null)
                _viewModel.SelectedArticle = null;
        }

        private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
        {
            AppNavigationView.IsPaneOpen = !AppNavigationView.IsPaneOpen;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedArticle))
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    AppNavigationView.IsBackEnabled = _viewModel.SelectedArticle != null;
                    AppTitleBar.IsBackButtonEnabled = _viewModel.SelectedArticle != null;
                });
            }
        }

        private void ConfigureSearch()
        {
            SearchBox.PlaceholderText = LocalizationManager.Current.SearchPlaceholder;
            LocalizationManager.LanguageChanged += (sender, args) =>
            {
                SearchBox.PlaceholderText = LocalizationManager.Current.SearchPlaceholder;
            };

            SearchBox.TextChanged += (sender, args) =>
            {
                if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    _viewModel.SearchQuery = SearchBox.Text;
                    if (string.IsNullOrEmpty(SearchBox.Text))
                        SafeFireAndForget.Run(() => _viewModel.SyncFeedsAsync());
                }
            };

            SearchBox.QuerySubmitted += (sender, args) =>
            {
                _viewModel.SearchQuery = SearchBox.Text;
                SafeFireAndForget.Run(() => _viewModel.SyncFeedsAsync());
            };
        }

        private void BuildSubPanels()
        {
            _articlesPage = new ArticlesPage();
            _articlesPage.Initialize(_viewModel);

            _settingsPage = new SettingsPage();
            _settingsPage.Initialize(_viewModel);

            ContentGrid.Children.Clear();
            ContentGrid.Children.Add(_articlesPage);

            LocalizationManager.LanguageChanged += (s, ev) =>
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    var settingsItem = AppNavigationView.SettingsItem as NavigationViewItem;
                    if (settingsItem != null) settingsItem.Content = LocalizationManager.Current.SettingsTab;
                    RebuildNavigationMenu();
                });
            };
        }

        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_articlesPage == null || _settingsPage == null) return;
            if (_isRebuildingNavigation) return;

            ContentGrid.Children.Clear();
            if (args.IsSettingsSelected)
            {
                ContentGrid.Children.Add(_settingsPage);
                AppNavigationView.IsBackEnabled = false;
            }
            else
            {
                ContentGrid.Children.Add(_articlesPage);
                _articlesPage.ApplyLayoutMode();
                AppNavigationView.IsBackEnabled = _viewModel.SelectedArticle != null;
            }

            if (!args.IsSettingsSelected)
            {
                var selectedItem = args.SelectedItem as NavigationViewItem;
                if (selectedItem != null)
                {
                    if (selectedItem.Tag as string == "all_articles")
                        _viewModel.SelectAllArticles();
                    else if (selectedItem.Tag is RssCategory category)
                        _viewModel.SelectCategory(category);
                    else if (selectedItem.Tag is RssFeed feed)
                        _viewModel.SelectFeed(feed);
                }
            }
        }

        private void RebuildNavigationMenu()
        {
            _isRebuildingNavigation = true;
            try
            {
                foreach (var cleanup in _menuEventCleanupActions)
                    cleanup();
                _menuEventCleanupActions.Clear();

                object? selectedTag = (AppNavigationView.SelectedItem as NavigationViewItem)?.Tag;

                AppNavigationView.MenuItems.Clear();

                var allArticlesItem = new NavigationViewItem
                {
                    Content = LocalizationManager.Current.FeedsTab,
                    Icon = new SymbolIcon(Symbol.Document),
                    Tag = "all_articles"
                };

                var totalBadge = new InfoBadge { Value = _viewModel.UnreadCount };
                totalBadge.Visibility = _viewModel.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                allArticlesItem.InfoBadge = totalBadge;

                PropertyChangedEventHandler totalBadgeHandler = (s, ev) =>
                {
                    if (ev.PropertyName == nameof(MainViewModel.UnreadCount))
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            totalBadge.Value = _viewModel.UnreadCount;
                            totalBadge.Visibility = _viewModel.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                        });
                };
                _viewModel.PropertyChanged += totalBadgeHandler;
                _menuEventCleanupActions.Add(() => _viewModel.PropertyChanged -= totalBadgeHandler);

                AppNavigationView.MenuItems.Add(allArticlesItem);
                AppNavigationView.MenuItems.Add(new NavigationViewItemSeparator());

                foreach (var category in _viewModel.Categories)
                {
                    var categoryItem = new NavigationViewItem
                    {
                        Content = category.Title,
                        Icon = new SymbolIcon(Symbol.Folder),
                        Tag = category,
                        IsExpanded = true
                    };

                    var catBadge = new InfoBadge { Value = category.UnreadCount };
                    catBadge.Visibility = category.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                    categoryItem.InfoBadge = catBadge;

                    PropertyChangedEventHandler catBadgeHandler = (s, ev) =>
                    {
                        if (ev.PropertyName == nameof(RssCategory.UnreadCount))
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                catBadge.Value = category.UnreadCount;
                                catBadge.Visibility = category.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                            });
                    };
                    category.PropertyChanged += catBadgeHandler;
                    _menuEventCleanupActions.Add(() => category.PropertyChanged -= catBadgeHandler);

                    foreach (var feed in category.Feeds)
                    {
                        var imageIcon = new ImageIcon();
                        try
                        {
                            if (!string.IsNullOrEmpty(feed.IconUrl))
                                imageIcon.Source = new BitmapImage(new Uri(feed.IconUrl));
                        }
                        catch { }

                        var feedItem = new NavigationViewItem
                        {
                            Content = feed.Title,
                            Icon = imageIcon,
                            Tag = feed
                        };

                        var feedBadge = new InfoBadge { Value = feed.UnreadCount };
                        feedBadge.Visibility = feed.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                        feedItem.InfoBadge = feedBadge;

                        PropertyChangedEventHandler feedBadgeHandler = (s, ev) =>
                        {
                            if (ev.PropertyName == nameof(RssFeed.UnreadCount))
                                this.DispatcherQueue.TryEnqueue(() =>
                                {
                                    feedBadge.Value = feed.UnreadCount;
                                    feedBadge.Visibility = feed.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                                });
                        };
                        feed.PropertyChanged += feedBadgeHandler;
                        _menuEventCleanupActions.Add(() => feed.PropertyChanged -= feedBadgeHandler);

                        categoryItem.MenuItems.Add(feedItem);
                    }

                    AppNavigationView.MenuItems.Add(categoryItem);
                }

                if (selectedTag != null)
                {
                    var matchedItem = FindMenuItemByTag(AppNavigationView.MenuItems, selectedTag);
                    AppNavigationView.SelectedItem = matchedItem ?? allArticlesItem;
                }
                else
                {
                    AppNavigationView.SelectedItem = allArticlesItem;
                }
            }
            finally
            {
                _isRebuildingNavigation = false;
            }
        }

        private static NavigationViewItem? FindMenuItemByTag(IList<object> items, object tag)
        {
            foreach (var item in items)
            {
                if (item is NavigationViewItem navItem)
                {
                    if (Equals(navItem.Tag, tag)) return navItem;
                    var matched = FindMenuItemByTag(navItem.MenuItems, tag);
                    if (matched != null) return matched;
                }
            }
            return null;
        }
    
        public void ActivateFromExternal()
        {
            _trayIconHelper.RestoreFromTray();
        }

        public void ActivateFromToast(string argument)
        {
            _trayIconHelper.RestoreFromTray();

            if (argument.StartsWith("articleId="))
            {
                var articleId = argument.Substring("articleId=".Length);
                _viewModel.SelectAllArticles();
                _viewModel.SelectArticleById(articleId);
            }
        }
    }
}
