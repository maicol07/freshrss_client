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
                AccountExpander.Header = LocalizationManager.CurrentLanguageCode == "it" ? "Account FreshRSS" : "FreshRSS Account";
                AccountExpander.Description = LocalizationManager.CurrentLanguageCode == "it" 
                    ? "Gestisci l'indirizzo del server e le tue credenziali di accesso" 
                    : "Manage your server address and login credentials";

                ServerUrlCard.Header = LocalizationManager.Current.ServerUrlLabel;
                ServerUrlCard.Description = LocalizationManager.CurrentLanguageCode == "it" 
                    ? "L'URL dell'API Google Reader del tuo FreshRSS" 
                    : "The Google Reader API URL of your FreshRSS server";

                UsernameCard.Header = LocalizationManager.Current.UsernameLabel;
                UsernameCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Il tuo nome utente di FreshRSS"
                    : "Your FreshRSS username";

                PasswordCard.Header = LocalizationManager.Current.ApiPasswordLabel;
                PasswordCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "La chiave API configurata nel tuo profilo FreshRSS"
                    : "The API password/token configured in your FreshRSS profile";

                // Sync Expander
                SyncExpander.Header = LocalizationManager.CurrentLanguageCode == "it" ? "Sincronizzazione e Cache" : "Synchronization & Cache";
                SyncExpander.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Configura la frequenza di aggiornamento e i limiti della cache"
                    : "Configure update frequency and cache limitations";

                IntervalCard.Header = LocalizationManager.Current.UpdateIntervalLabel;
                IntervalCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Frequenza di aggiornamento in background dei feed"
                    : "How often the app refreshes feeds in the background";

                MaxReadCard.Header = LocalizationManager.Current.MaxReadArticlesLabel;
                MaxReadCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Numero massimo di articoli già letti da mantenere sincronizzati offline"
                    : "Maximum number of read articles to keep synchronized offline";

                // Reading Expander
                ReadingExpander.Header = LocalizationManager.CurrentLanguageCode == "it" ? "Opzioni di Lettura" : "Reading Preferences";
                ReadingExpander.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Personalizza il comportamento di lettura e il recupero dei contenuti"
                    : "Customize reading behavior and content fetching";

                OpenGraphCard.Header = LocalizationManager.Current.EnableOpenGraphLabel;
                OpenGraphCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Scarica immagini di copertina e testo completo degli articoli se non forniti dai feed"
                    : "Download cover images and full article text if not provided directly by feeds";

                DefaultFilterCard.Header = LocalizationManager.CurrentLanguageCode == "it" ? "Filtro predefinito" : "Default filter";
                DefaultFilterCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Scegli quale filtro applicare automaticamente all'avvio dell'applicazione"
                    : "Select which filter to automatically apply when starting the application";

                int prevFilterIdx = DefaultFilterComboBox.SelectedIndex;
                DefaultFilterComboBox.Items.Clear();
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterAll);
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterUnread);
                DefaultFilterComboBox.Items.Add(LocalizationManager.Current.FilterRead);
                DefaultFilterComboBox.SelectedIndex = prevFilterIdx >= 0 ? prevFilterIdx : 0;

                OpenInBrowserCard.Header = LocalizationManager.Current.OpenLinksInBrowserLabel;
                OpenInBrowserCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Apri direttamente i link nel browser esterno invece del visualizzatore integrato"
                    : "Directly open links in your default external browser instead of the built-in viewer";

                // System Expander
                SystemExpander.Header = LocalizationManager.CurrentLanguageCode == "it" ? "Integrazione di Sistema" : "System Integration";
                SystemExpander.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Configura l'avvio automatico e le opzioni della barra delle applicazioni"
                    : "Configure automatic startup and system tray options";

                AutoStartCard.Header = LocalizationManager.Current.AutoStartLabel;
                AutoStartCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Avvia automaticamente l'applicazione all'accesso a Windows"
                    : "Automatically launch the application when signing in to Windows";

                StartMinimizedCard.Header = LocalizationManager.Current.StartMinimizedLabel;
                StartMinimizedCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Avvia l'applicazione ridotta nell'area di notifica (tray icon)"
                    : "Start the application minimized in the system tray notification area";

                // Language Card
                LanguageCard.Header = LocalizationManager.Current.LanguageLabel;
                LanguageCard.Description = LocalizationManager.CurrentLanguageCode == "it"
                    ? "Scegli la lingua per l'interfaccia dell'applicazione"
                    : "Choose the language for the application's user interface";
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
