using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace FreshRssClient.Helpers
{
    public static class StartupHelper
    {
        public static bool IsPackaged()
        {
            try
            {
                // Accessing Package.Current throws if running unpackaged
                return Windows.ApplicationModel.Package.Current != null && 
                       Windows.ApplicationModel.Package.Current.Id != null;
            }
            catch
            {
                return false;
            }
        }

        public static async Task SetStartupAsync(bool enable, bool startMinimized)
        {
            if (IsPackaged())
            {
                try
                {
                    var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("FreshRssClientStartup");
                    if (startupTask != null)
                    {
                        if (enable)
                        {
                            if (startupTask.State == Windows.ApplicationModel.StartupTaskState.Disabled)
                            {
                                await startupTask.RequestEnableAsync();
                            }
                        }
                        else
                        {
                            if (startupTask.State == Windows.ApplicationModel.StartupTaskState.Enabled)
                            {
                                startupTask.Disable();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to set packaged startup task: {ex.Message}");
                }
            }
            else
            {
                // Registry startup configuration for unpackaged run
                string exePath = Environment.ProcessPath ?? 
                                 System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? 
                                 string.Empty;
                
                if (string.IsNullOrEmpty(exePath)) return;

                string args = startMinimized ? " --minimized" : "";
                string value = $"\"{exePath}\"{args}";

                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue("FreshRssClient", value);
                        }
                        else
                        {
                            key.DeleteValue("FreshRssClient", false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to set registry run value: {ex.Message}");
                }
            }
        }
    }
}
