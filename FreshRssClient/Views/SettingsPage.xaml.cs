using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.WinUI.Controls;
using FreshRssClient.ViewModels;
using FreshRssClient.Services;

namespace FreshRssClient.Views
{
    public sealed partial class SettingsPage : Page
    {
        private MainViewModel? _viewModel;
        private bool _isUpdating = false;

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        public void Initialize(MainViewModel viewModel)
        {
            _viewModel = viewModel;

            // Load initial values to controls
            _isUpdating = true;
            try
            {
                ServerUrlInput.Text = _viewModel.ServerUrl;
                UsernameInput.Text = _viewModel.Username;
                PasswordInput.Password = _viewModel.ApiPassword;

                IntervalNumberBox.Value = _viewModel.SyncInterval;
                MaxReadNumberBox.Value = _viewModel.MaxReadArticles;

                OpenGraphToggle.IsOn = _viewModel.EnableOpenGraph;
                OpenInBrowserToggle.IsOn = _viewModel.OpenLinksInBrowser;

                AutoStartToggle.IsOn = _viewModel.AutoStartWithWindows;
                StartMinimizedToggle.IsOn = _viewModel.StartMinimizedInTray;
                StartMinimizedToggle.IsEnabled = _viewModel.AutoStartWithWindows;

                // Populate and select default filter
                DefaultFilterComboBox.Items.Clear();
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterAll);
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterUnread);
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterRead);
                
                var filterItems = new List<string> { "All", "Unread", "Read" };
                int filterIndex = filterItems.IndexOf(_viewModel.ArticleFilter);
                DefaultFilterComboBox.SelectedIndex = filterIndex >= 0 ? filterIndex : 0;

                // Populate language ComboBox
                LanguageComboBox.Items.Clear();
                LanguageComboBox.Items.Add("Italiano");
                LanguageComboBox.Items.Add("English");
                LanguageComboBox.SelectedIndex = _viewModel.Language == "it" ? 0 : 1;

                StatusText.Text = _viewModel.ConnectionStatusText;
            }
            finally
            {
                _isUpdating = false;
            }

            UpdateLocalizations();

            // Subscribe to VM updates
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Subscribe to language updates
            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null || _isUpdating) return;

            this.DispatcherQueue.TryEnqueue(() =>
            {
                _isUpdating = true;
                try
                {
                    switch (e.PropertyName)
                    {
                        case nameof(MainViewModel.ServerUrl):
                            ServerUrlInput.Text = _viewModel.ServerUrl;
                            break;
                        case nameof(MainViewModel.Username):
                            UsernameInput.Text = _viewModel.Username;
                            break;
                        case nameof(MainViewModel.ApiPassword):
                            PasswordInput.Password = _viewModel.ApiPassword;
                            break;
                        case nameof(MainViewModel.SyncInterval):
                            IntervalNumberBox.Value = _viewModel.SyncInterval;
                            break;
                        case nameof(MainViewModel.MaxReadArticles):
                            MaxReadNumberBox.Value = _viewModel.MaxReadArticles;
                            break;
                        case nameof(MainViewModel.EnableOpenGraph):
                            OpenGraphToggle.IsOn = _viewModel.EnableOpenGraph;
                            break;
                        case nameof(MainViewModel.OpenLinksInBrowser):
                            OpenInBrowserToggle.IsOn = _viewModel.OpenLinksInBrowser;
                            break;
                        case nameof(MainViewModel.AutoStartWithWindows):
                            AutoStartToggle.IsOn = _viewModel.AutoStartWithWindows;
                            StartMinimizedToggle.IsEnabled = _viewModel.AutoStartWithWindows;
                            break;
                        case nameof(MainViewModel.StartMinimizedInTray):
                            StartMinimizedToggle.IsOn = _viewModel.StartMinimizedInTray;
                            break;
                        case nameof(MainViewModel.ConnectionStatusText):
                            StatusText.Text = _viewModel.ConnectionStatusText;
                            break;
                        case nameof(MainViewModel.ArticleFilter):
                            var filterItems = new List<string> { "All", "Unread", "Read" };
                            int filterIndex = filterItems.IndexOf(_viewModel.ArticleFilter);
                            if (filterIndex >= 0 && DefaultFilterComboBox.SelectedIndex != filterIndex)
                            {
                                DefaultFilterComboBox.SelectedIndex = filterIndex;
                            }
                            break;
                        case nameof(MainViewModel.Language):
                            LanguageComboBox.SelectedIndex = _viewModel.Language == "it" ? 0 : 1;
                            break;
                    }
                }
                finally
                {
                    _isUpdating = false;
                }
            });
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateLocalizations();
            });
        }

        private void UpdateLocalizations()
        {
            if (_viewModel == null) return;

            _isUpdating = true;
            try
            {
                SettingsTitle.Text = LocalizationManager.Current.SettingsTab;

                // Account Expander
                AccountExpander.Header = LocalizationManager.Current.AccountExpanderHeader;
                AccountExpander.Description = LocalizationManager.Current.AccountExpanderDesc;

                ServerUrlCard.Header = LocalizationManager.Current.ServerUrlLabel;
                ServerUrlCard.Description = LocalizationManager.Current.ServerUrlCardDesc;

                UsernameCard.Header = LocalizationManager.Current.UsernameLabel;
                UsernameCard.Description = LocalizationManager.Current.UsernameCardDesc;

                PasswordCard.Header = LocalizationManager.Current.ApiPasswordLabel;
                PasswordCard.Description = LocalizationManager.Current.PasswordCardDesc;

                // Sync Expander
                SyncExpander.Header = LocalizationManager.Current.SyncExpanderHeader;
                SyncExpander.Description = LocalizationManager.Current.SyncExpanderDesc;

                IntervalCard.Header = LocalizationManager.Current.UpdateIntervalLabel;
                IntervalCard.Description = LocalizationManager.Current.IntervalCardDesc;

                MaxReadCard.Header = LocalizationManager.Current.MaxReadArticlesLabel;
                MaxReadCard.Description = LocalizationManager.Current.MaxReadCardDesc;

                // Reading Expander
                ReadingExpander.Header = LocalizationManager.Current.ReadingExpanderHeader;
                ReadingExpander.Description = LocalizationManager.Current.ReadingExpanderDesc;

                OpenGraphCard.Header = LocalizationManager.Current.EnableOpenGraphLabel;
                OpenGraphCard.Description = LocalizationManager.Current.OpenGraphCardDesc;

                DefaultFilterCard.Header = LocalizationManager.Current.DefaultFilterCardHeader;
                DefaultFilterCard.Description = LocalizationManager.Current.DefaultFilterCardDesc;

                int prevFilterIdx = DefaultFilterComboBox.SelectedIndex;
                DefaultFilterComboBox.Items.Clear();
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterAll);
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterUnread);
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterRead);
                DefaultFilterComboBox.SelectedIndex = prevFilterIdx >= 0 ? prevFilterIdx : 0;

                OpenInBrowserCard.Header = LocalizationManager.Current.OpenLinksInBrowserLabel;
                OpenInBrowserCard.Description = LocalizationManager.Current.OpenInBrowserCardDesc;

                // System Expander
                SystemExpander.Header = LocalizationManager.Current.SystemExpanderHeader;
                SystemExpander.Description = LocalizationManager.Current.SystemExpanderDesc;

                AutoStartCard.Header = LocalizationManager.Current.AutoStartLabel;
                AutoStartCard.Description = LocalizationManager.Current.AutoStartCardDesc;

                StartMinimizedCard.Header = LocalizationManager.Current.StartMinimizedLabel;
                StartMinimizedCard.Description = LocalizationManager.Current.StartMinimizedCardDesc;

                // Language Card
                LanguageCard.Header = LocalizationManager.Current.LanguageLabel;
                LanguageCard.Description = LocalizationManager.Current.LanguageCardDesc;
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void OnServerUrlLostFocus(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && _viewModel.ServerUrl != ServerUrlInput.Text)
            {
                _viewModel.ServerUrl = ServerUrlInput.Text;
            }
        }

        private void OnUsernameLostFocus(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && _viewModel.Username != UsernameInput.Text)
            {
                _viewModel.Username = UsernameInput.Text;
            }
        }

        private void OnPasswordLostFocus(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && _viewModel.ApiPassword != PasswordInput.Password)
            {
                _viewModel.ApiPassword = PasswordInput.Password;
            }
        }

        private void OnIntervalLostFocus(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && !double.IsNaN(IntervalNumberBox.Value))
            {
                int val = (int)IntervalNumberBox.Value;
                if (_viewModel.SyncInterval != val)
                {
                    _viewModel.SyncInterval = val;
                }
            }
        }

        private void OnMaxReadLostFocus(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && !double.IsNaN(MaxReadNumberBox.Value))
            {
                int val = (int)MaxReadNumberBox.Value;
                if (_viewModel.MaxReadArticles != val)
                {
                    _viewModel.MaxReadArticles = val;
                }
            }
        }

        private void OnOpenGraphToggled(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && _viewModel.EnableOpenGraph != OpenGraphToggle.IsOn)
            {
                _viewModel.EnableOpenGraph = OpenGraphToggle.IsOn;
            }
        }

        private void OnDefaultFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && DefaultFilterComboBox.SelectedIndex >= 0)
            {
                var filterItems = new List<string> { "All", "Unread", "Read" };
                string selectedFilter = filterItems[DefaultFilterComboBox.SelectedIndex];
                if (_viewModel.ArticleFilter != selectedFilter)
                {
                    _viewModel.ArticleFilter = selectedFilter;
                }
            }
        }

        private void OnOpenInBrowserToggled(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && _viewModel.OpenLinksInBrowser != OpenInBrowserToggle.IsOn)
            {
                _viewModel.OpenLinksInBrowser = OpenInBrowserToggle.IsOn;
            }
        }

        private void OnAutoStartToggled(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating)
            {
                if (_viewModel.AutoStartWithWindows != AutoStartToggle.IsOn)
                {
                    _viewModel.AutoStartWithWindows = AutoStartToggle.IsOn;
                }
                StartMinimizedToggle.IsEnabled = AutoStartToggle.IsOn;
            }
        }

        private void OnStartMinimizedToggled(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && _viewModel.StartMinimizedInTray != StartMinimizedToggle.IsOn)
            {
                _viewModel.StartMinimizedInTray = StartMinimizedToggle.IsOn;
            }
        }

        private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel != null && !_isUpdating && LanguageComboBox.SelectedIndex >= 0)
            {
                string targetLang = LanguageComboBox.SelectedIndex == 0 ? "it" : "en";
                if (_viewModel.Language != targetLang)
                {
                    _viewModel.Language = targetLang;
                }
            }
        }
    }
}
