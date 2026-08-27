using System;
using System.IO;

namespace FreshRssClient.Helpers
{
    /// <summary>
    /// Appends diagnostic lines to %LOCALAPPDATA%\FreshRssClient\debug_log.txt so behaviour can be
    /// inspected without a debugger attached. Compiled out of Release builds.
    /// </summary>
    public static class DebugLog
    {
        private const long MaxFileBytes = 2 * 1024 * 1024;
        private static readonly object FileLock = new();

        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreshRssClient",
            "debug_log.txt");

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Write(string tag, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{tag}] {message}";
            System.Diagnostics.Debug.WriteLine(line);

            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);

                    var info = new FileInfo(LogFilePath);
                    if (info.Exists && info.Length > MaxFileBytes)
                    {
                        File.Delete(LogFilePath);
                    }

                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DebugLog] write failed: {ex.Message}");
                }
            }
        }
    }
}
