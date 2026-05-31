using System;
using System.Threading.Tasks;

namespace FreshRssClient.Helpers
{
    public static class SafeFireAndForget
    {
        public static async void Run(Func<Task> taskFactory, Action<Exception>? onError = null)
        {
            try
            {
                await taskFactory();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (onError != null)
                {
                    onError(ex);
                }
                else
                {
                    LogException(ex);
                }
            }
        }

        public static async void Run(Task task, Action<Exception>? onError = null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (onError != null)
                {
                    onError(ex);
                }
                else
                {
                    LogException(ex);
                }
            }
        }

        private static void LogException(Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SafeFireAndForget] Unhandled exception: {ex}");
                var localFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreshRssClient");
                System.IO.Directory.CreateDirectory(localFolder);
                var logPath = System.IO.Path.Combine(localFolder, "crash_log.txt");
                var msg = $"[{DateTime.Now}] Fire-and-forget Exception:\n{ex}\n\n";
                System.IO.File.AppendAllText(logPath, msg);
            }
            catch { }
        }
    }
}
