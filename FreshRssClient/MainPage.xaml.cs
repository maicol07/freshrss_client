using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using FreshRssClient.ViewModels;
using FreshRssClient.Services;
using FreshRssClient.Views;

namespace FreshRssClient
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel? ViewModel { get; private set; }

        private ArticlesPage? _articlesPage;
        private SettingsPage? _settingsPage;
        
        private bool _isRebuildingNavigation = false;
        private readonly List<Action> _menuEventCleanupActions = new();

        public MainPage()
        {
            this.InitializeComponent();
        }

        public void Initialize(MainViewModel viewModel)
        {
            ViewModel = viewModel;
            BuildSubPanels();

            // Subscribe to dynamic sidebar structure updates
            ViewModel.NavigationStructureChanged += (s, ev) =>
            {
                this.DispatcherQueue.TryEnqueue(() => RebuildNavigationMenu());
            };

            RebuildNavigationMenu();
        }

        private void BuildSubPanels()
        {
            if (ViewModel == null) return;

            // 1. Create Articles Page
            _articlesPage = new ArticlesPage();
            _articlesPage.Initialize(ViewModel);

            // 2. Create Settings Page
            _settingsPage = new SettingsPage();
            _settingsPage.Initialize(ViewModel);

            // Set default view content
            ContentGrid.Children.Clear();
            ContentGrid.Children.Add(_articlesPage);

            // Hook up language updates
            LocalizationManager.LanguageChanged += (s, ev) =>
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    var settingsItem = MainNavigationView.SettingsItem as NavigationViewItem;
                    if (settingsItem != null) settingsItem.Content = LocalizationManager.Current.SettingsTab;
                    RebuildNavigationMenu();
                });
            };
        }

        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_articlesPage == null || _settingsPage == null || ViewModel == null) return;
            if (_isRebuildingNavigation) return;

            ContentGrid.Children.Clear();
            if (args.IsSettingsSelected)
            {
                ContentGrid.Children.Add(_settingsPage);
            }
            else
            {
                ContentGrid.Children.Add(_articlesPage);
                _articlesPage.ApplyLayoutMode(); // Ensure layout displays correctly on return
            }

            if (!args.IsSettingsSelected)
            {
                var selectedItem = args.SelectedItem as NavigationViewItem;
                if (selectedItem != null)
                {
                    if (selectedItem.Tag as string == "all_articles")
                    {
                        ViewModel.SelectAllArticles();
                    }
                    else if (selectedItem.Tag is RssCategory category)
                    {
                        ViewModel.SelectCategory(category);
                    }
                    else if (selectedItem.Tag is RssFeed feed)
                    {
                        ViewModel.SelectFeed(feed);
                    }
                }
            }
        }

        private void RebuildNavigationMenu()
        {
            if (ViewModel == null) return;

            _isRebuildingNavigation = true;
            try
            {
                // Unhook previous event handlers to prevent memory leaks
                foreach (var cleanup in _menuEventCleanupActions)
                {
                    cleanup();
                }
                _menuEventCleanupActions.Clear();

                // Save currently selected tag
                object? selectedTag = (MainNavigationView.SelectedItem as NavigationViewItem)?.Tag;

                MainNavigationView.MenuItems.Clear();

                // 1. All Articles
                var allArticlesItem = new NavigationViewItem
                {
                    Content = LocalizationManager.Current.FeedsTab,
                    Icon = new SymbolIcon(Symbol.Document),
                    Tag = "all_articles"
                };

                var totalBadge = new InfoBadge { Value = ViewModel.UnreadCount };
                totalBadge.Visibility = ViewModel.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                allArticlesItem.InfoBadge = totalBadge;

                PropertyChangedEventHandler totalBadgeHandler = (s, ev) =>
                {
                    if (ev.PropertyName == nameof(MainViewModel.UnreadCount))
                    {
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            totalBadge.Value = ViewModel.UnreadCount;
                            totalBadge.Visibility = ViewModel.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                        });
                    }
                };
                ViewModel.PropertyChanged += totalBadgeHandler;
                _menuEventCleanupActions.Add(() => ViewModel.PropertyChanged -= totalBadgeHandler);

                MainNavigationView.MenuItems.Add(allArticlesItem);

                // 2. Add divider
                MainNavigationView.MenuItems.Add(new NavigationViewItemSeparator());

                // 3. Add dynamic categories and feeds
                foreach (var category in ViewModel.Categories)
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
                        {
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                catBadge.Value = category.UnreadCount;
                                catBadge.Visibility = category.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                            });
                        }
                    };
                    category.PropertyChanged += catBadgeHandler;
                    _menuEventCleanupActions.Add(() => category.PropertyChanged -= catBadgeHandler);

                    // Add child feeds
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
                            {
                                this.DispatcherQueue.TryEnqueue(() =>
                                {
                                    feedBadge.Value = feed.UnreadCount;
                                    feedBadge.Visibility = feed.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                                });
                            }
                        };
                        feed.PropertyChanged += feedBadgeHandler;
                        _menuEventCleanupActions.Add(() => feed.PropertyChanged -= feedBadgeHandler);

                        categoryItem.MenuItems.Add(feedItem);
                    }

                    MainNavigationView.MenuItems.Add(categoryItem);
                }

                // Restore selection
                if (selectedTag != null)
                {
                    var matchedItem = FindMenuItemByTag(MainNavigationView.MenuItems, selectedTag);
                    if (matchedItem != null)
                    {
                        MainNavigationView.SelectedItem = matchedItem;
                    }
                    else
                    {
                        MainNavigationView.SelectedItem = allArticlesItem;
                    }
                }
                else
                {
                    MainNavigationView.SelectedItem = allArticlesItem;
                }
            }
            finally
            {
                _isRebuildingNavigation = false;
            }
        }

        private NavigationViewItem? FindMenuItemByTag(IList<object> items, object tag)
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
    }
}
