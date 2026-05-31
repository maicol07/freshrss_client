using System;
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

        public MainWindow()
        {
            this.InitializeComponent();

            try
            {
                // 1. Establish Mica Backdrop
                this.SystemBackdrop = new MicaBackdrop();

                // 2. Integrate TitleBar content into shell
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);
                AppWindow.SetIcon("Assets/AppIcon.ico");

                // 3. Instantiate MainViewModel and Wire System Tray integration
                _viewModel = new MainViewModel();

                _trayIconHelper = new TrayIconHelper(this, () =>
                {
                    if (_viewModel.SyncCommand.CanExecute(null))
                    {
                        _viewModel.SyncCommand.Execute(null);
                    }
                });

                _viewModel.RegisterTrayIconHelper(_trayIconHelper);

                // Minimize to tray on close
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

                // Start minimized checks
                string[] cmdArgs = Environment.GetCommandLineArgs();
                bool startMinimized = cmdArgs.Contains("--minimized", StringComparer.OrdinalIgnoreCase) || 
                                     cmdArgs.Contains("-minimized", StringComparer.OrdinalIgnoreCase) ||
                                     _viewModel.StartMinimizedInTray;

                if (startMinimized)
                {
                    this.Activated += (sender, args) =>
                    {
                        if (startMinimized)
                        {
                            startMinimized = false;
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                _trayIconHelper.MinimizeToTray();
                            });
                        }
                    };
                }

                // 4. Load MainPage
                var mainPage = new MainPage();
                mainPage.Initialize(_viewModel);
                MainContent.Content = mainPage;

                // 5. Configure search box localization and events
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
                        {
                            SafeFireAndForget.Run(() => _viewModel.SyncFeedsAsync());
                        }
                    }
                };

                SearchBox.QuerySubmitted += (sender, args) =>
                {
                    _viewModel.SearchQuery = SearchBox.Text;
                    SafeFireAndForget.Run(() => _viewModel.SyncFeedsAsync());
                };
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
    }
}
