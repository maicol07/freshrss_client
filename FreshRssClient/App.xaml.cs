using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;

namespace FreshRssClient;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.UnhandledException += (s, e) =>
        {
            WriteCrashLog("XAML Unhandled Exception", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            WriteCrashLog("AppDomain Unhandled Exception", e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            WriteCrashLog("Unobserved Task Exception", e.Exception);
            e.SetObserved();
        };
        InitializeComponent();
    }

    private static void WriteCrashLog(string type, Exception? ex)
    {
        try
        {
            var localFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreshRssClient");
            System.IO.Directory.CreateDirectory(localFolder);
            var logPath = System.IO.Path.Combine(localFolder, "crash_log.txt");

            var msg = $"[{DateTime.Now}] {type}:\n{ex?.ToString() ?? "No exception details"}\n\n";
            System.IO.File.AppendAllText(logPath, msg);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string AppID);

    private static bool IsPackaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            try
            {
                if (IsPackaged())
                {
                    var packageAumid = $"{Windows.ApplicationModel.Package.Current.Id.FamilyName}!App";
                    SetCurrentProcessExplicitAppUserModelID(packageAumid);
                }
                else
                {
                    SetCurrentProcessExplicitAppUserModelID("Maicol.FreshRssClient.App");
                }
            }
            catch
            {
                // Fallback for environments where COM/shell32 explicit AppID registration fails
            }

            var thisInstance = AppInstance.FindOrRegisterForKey("FreshRssClient_SingleInstance");

            if (!thisInstance.IsCurrent)
            {
                thisInstance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
                Environment.Exit(0);
                return;
            }

            thisInstance.Activated += OnAppInstanceActivated;

            _mainWindow = new MainWindow();
            if (!_mainWindow.ShouldStartMinimized)
            {
                _mainWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            try
            {
                var localFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreshRssClient");
                System.IO.Directory.CreateDirectory(localFolder);
                var logPath = System.IO.Path.Combine(localFolder, "crash_log.txt");
                System.IO.File.WriteAllText(logPath, "App Exception:\n" + ex.ToString());
            }
            catch { }
            throw;
        }
    }

    private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        if (_mainWindow == null) return;

        _mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Data is ToastNotificationActivatedEventArgs toastArgs)
            {
                _mainWindow.ActivateFromToast(toastArgs.Argument);
            }
            else
            {
                _mainWindow.ActivateFromExternal();
            }
        });
    }
}
